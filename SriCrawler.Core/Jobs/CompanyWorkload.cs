using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DescagaCompronanteSRI.Jobs;

[JsonConverter(typeof(ApiEnumConverter<WorkloadLimitCode>))]
public enum WorkloadLimitCode { CompanyPendingLimitReached, CompanyUsageLimitReached }
public sealed record WorkloadLimitResponse(WorkloadLimitCode Code, string Message, int Limit, int Current,
    DateTimeOffset? RetryAt);
public sealed class WorkloadLimitExceededException(WorkloadLimitResponse result) : Exception
{
    public WorkloadLimitResponse Result { get; } = result;
}

public static class CompanyWorkload
{
    // A stable namespace keeps admission and execution locks independent of the taxpayer lock.
    public static long LockKey(string name) => BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    public static async Task CheckAdmissionAsync(CrawlerDbContext db, string companyId, ExtractionJobOptions options,
        DateTimeOffset now, CancellationToken token)
    {
        var company = db.Extractions.AsNoTracking().Where(x => x.CompanyId == companyId);
        var pending = await company.CountAsync(x => x.JobStatus != JobStatus.Completed && x.JobStatus != JobStatus.Failed && x.ExpiresAt > now, token);
        if (pending >= options.MaxPendingPerCompany)
            throw new WorkloadLimitExceededException(new(WorkloadLimitCode.CompanyPendingLimitReached,
                "The company has reached its pending extraction limit. Retry after an extraction finishes.", options.MaxPendingPerCompany, pending, null));
        var windowStart = now.AddHours(-24);
        var recent = company.Where(x => x.CreatedAt > windowStart);
        var accepted = await recent.CountAsync(token);
        if (accepted >= options.MaxAcceptedPerCompany24Hours)
        {
            // If the configured quota was reduced, enough prior acceptances must leave the window.
            var release = await recent.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(accepted - options.MaxAcceptedPerCompany24Hours).Select(x => x.CreatedAt).FirstAsync(token);
            throw new WorkloadLimitExceededException(new(WorkloadLimitCode.CompanyUsageLimitReached,
                "The company has reached its accepted extraction limit for the rolling 24-hour window.",
                options.MaxAcceptedPerCompany24Hours, accepted, release.AddHours(24)));
        }
    }

    public static async Task<bool> TryAcquireExecutionSlotAsync(NpgsqlConnection connection, string companyId, int limit, CancellationToken token)
    {
        for (var slot = 0; slot < limit; slot++)
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey($"sri-company-execution:{companyId}:{slot}"));
            if (Equals(await command.ExecuteScalarAsync(token), true)) return true;
        }
        return false;
    }
}
