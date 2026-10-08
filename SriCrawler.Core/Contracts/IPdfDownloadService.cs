using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Contracts;

public interface IPdfDownloadService
{
    Task<bool> DownloadAsync(
        PlaywrightSession session,
        string pdfLinkId,
        string destinationPath,
        string logPrefix = "[PDF]");
}
