using DescagaCompronanteSRI.Services;

namespace DescagaCompronanteSRI.Tests.Services;

public class FileNameSanitizerTests
{
    [Fact]
    public void Sanitize_ReplacesInvalidFileNameCharacters()
    {
        var sanitized = FileNameSanitizer.Sanitize("abc/def:ghi*pdf");

        Assert.Equal("abc_def_ghi_pdf", sanitized);
    }

    [Fact]
    public void CombineSafe_AppendsNormalizedExtensionAndFallback()
    {
        var path = PathService.CombineSafe("/tmp/sri", "", "pdf", "sin_clave");

        Assert.Equal(Path.Combine("/tmp/sri", "sin_clave.pdf"), path);
    }
}
