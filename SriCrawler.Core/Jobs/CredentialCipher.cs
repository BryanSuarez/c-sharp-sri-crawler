using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Jobs;

public sealed class CredentialCipher(IOptions<ExtractionJobOptions> options)
{
    public static bool IsValidKey(string value)
    {
        try { return Convert.FromBase64String(value).Length == 32; }
        catch (FormatException) { return false; }
    }
    public string Encrypt(string password, Guid extractionId)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.EncryptionKey), 16);
        aes.Encrypt(nonce, plain, cipher, tag, extractionId.ToByteArray());
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public string Decrypt(string encrypted, Guid extractionId)
    {
        var value = Convert.FromBase64String(encrypted);
        var plain = new byte[value.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.EncryptionKey), 16);
        aes.Decrypt(value.AsSpan(0, 12), value.AsSpan(28), value.AsSpan(12, 16), plain, extractionId.ToByteArray());
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
