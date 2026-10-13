using System.Globalization;
using System.Security.Cryptography;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DescagaCompronanteSRI.Security;

public static class ApiKeyToken
{
    public static (Guid Id, string Token, byte[] Hash) Create()
    {
        var id = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (id, $"sri_{id:N}.{secret}", SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(secret)));
    }

    public static bool TryParse(string value, out Guid id, out byte[] hash)
    {
        id = default; hash = [];
        if (value.Length != 80 || !value.StartsWith("sri_", StringComparison.Ordinal) || value[36] != '.'
            || !Guid.TryParseExact(value.AsSpan(4, 32), "N", out id)) return false;
        var secret = value.AsSpan(37);
        foreach (var c in secret) if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return false;
        // Require the canonical encoding of exactly 32 random bytes.
        try
        {
            var bytes = Convert.FromBase64String(secret.ToString().Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32 || Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != secret.ToString()) return false;
        }
        catch (FormatException) { return false; }
        hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(secret.ToString())); return true;
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(DescagaCompronanteSRI.Serialization.ApiEnumConverter<ApiKeyState>))]
public enum ApiKeyState { Active, Expired, Revoked }
public sealed record CompanyApiKeySummary(Guid KeyId, string CompanyId, string Name, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, ApiKeyState Status);
public sealed record CreatedCompanyApiKey(CompanyApiKeySummary Key, string ApiKey);
public sealed record ApiKeyIdentity(Guid KeyId, string CompanyId);
public sealed class ApiKeyPersistenceUnavailableException : Exception;
internal static class SecurityPersistence
{
    public static bool IsDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is NpgsqlException) return true;
        return false;
    }
}
public interface ICompanyApiKeys
{
    Task<ApiKeyIdentity?> AuthenticateAsync(Guid id, byte[] hash, CancellationToken token);
    Task<CreatedCompanyApiKey> CreateAsync(string companyId, string name, DateTimeOffset? expiresAt, Guid administratorId, CancellationToken token);
    Task<CursorPage<CompanyApiKeySummary>> ListAsync(string companyId, long cursor, int limit, CancellationToken token);
    Task<bool> RevokeAsync(string companyId, Guid keyId, Guid administratorId, CancellationToken token);
}

public sealed class CompanyApiKeys(CrawlerDbContext db, TimeProvider clock, int defaultLifetimeDays) : ICompanyApiKeys
{
    private static CompanyApiKeySummary Summary(CompanyApiKey key, DateTimeOffset now) => new(key.PublicId, key.CompanyId,
        key.Name, key.CreatedAt, key.ExpiresAt, key.RevokedAt,
        key.RevokedAt is not null ? ApiKeyState.Revoked : key.ExpiresAt <= now ? ApiKeyState.Expired : ApiKeyState.Active);

    public async Task<ApiKeyIdentity?> AuthenticateAsync(Guid id, byte[] hash, CancellationToken token)
    {
        try
        {
            var key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == id, token);
            return key is not null && CryptographicOperations.FixedTimeEquals(key.SecretHash, hash)
                && key.RevokedAt is null && key.ExpiresAt > clock.GetUtcNow() ? new(key.PublicId, key.CompanyId) : null;
        }
        catch (Exception e) when (SecurityPersistence.IsDatabaseFailure(e)) { throw new ApiKeyPersistenceUnavailableException(); }
    }

    public async Task<CreatedCompanyApiKey> CreateAsync(string companyId, string name, DateTimeOffset? expiresAt, Guid administratorId, CancellationToken token)
    {
        var now = clock.GetUtcNow(); var expiration = expiresAt?.ToUniversalTime() ?? now.AddDays(defaultLifetimeDays);
        if (expiration <= now) throw new ArgumentException("Expiration must be in the future.", nameof(expiresAt));
        var generated = ApiKeyToken.Create();
        var key = new CompanyApiKey { PublicId = generated.Id, CompanyId = companyId, Name = name.Trim(), SecretHash = generated.Hash,
            CreatedAt = now, ExpiresAt = expiration, CreatedBy = administratorId };
        db.ApiKeys.Add(key);
        try { await db.SaveChangesAsync(token); }
        catch (Exception e) when (SecurityPersistence.IsDatabaseFailure(e)) { throw new ApiKeyPersistenceUnavailableException(); }
        return new(Summary(key, now), generated.Token);
    }

    public async Task<CursorPage<CompanyApiKeySummary>> ListAsync(string companyId, long cursor, int limit, CancellationToken token)
    {
        try
        {
            var keys = await db.ApiKeys.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id > cursor)
                .OrderBy(x => x.Id).Take(limit + 1).ToListAsync(token);
            return new(keys.Take(limit).Select(x => Summary(x, clock.GetUtcNow())).ToArray(),
                keys.Count > limit ? keys[limit - 1].Id.ToString(CultureInfo.InvariantCulture) : null);
        }
        catch (Exception e) when (SecurityPersistence.IsDatabaseFailure(e)) { throw new ApiKeyPersistenceUnavailableException(); }
    }

    public async Task<bool> RevokeAsync(string companyId, Guid keyId, Guid administratorId, CancellationToken token)
    {
        try
        {
            var key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.PublicId == keyId, token);
            if (key is null) return false;
            await db.ApiKeys.Where(x => x.CompanyId == companyId && x.PublicId == keyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, clock.GetUtcNow())
                    .SetProperty(x => x.RevokedBy, administratorId), token);
            return true;
        }
        catch (Exception e) when (SecurityPersistence.IsDatabaseFailure(e)) { throw new ApiKeyPersistenceUnavailableException(); }
    }
}
