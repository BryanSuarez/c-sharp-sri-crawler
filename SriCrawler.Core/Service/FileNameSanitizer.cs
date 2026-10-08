using System.Text.RegularExpressions;

namespace DescagaCompronanteSRI.Services;

public static class FileNameSanitizer
{
    private static readonly Regex InvalidFileNameChars = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);

    public static string Sanitize(string? fileName, string fallback = "sin_nombre")
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fallback;
        }

        var sanitized = InvalidFileNameChars.Replace(fileName.Trim(), "_");
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
