namespace DescagaCompronanteSRI.Services;

public static class PathService
{
    public static string CombineSafe(
        string directory,
        string? fileNameWithoutExtension,
        string extension,
        string fallbackFileName = "sin_nombre")
    {
        var safeFileName = FileNameSanitizer.Sanitize(fileNameWithoutExtension, fallbackFileName);
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";

        return Path.Combine(directory, safeFileName + normalizedExtension);
    }
}
