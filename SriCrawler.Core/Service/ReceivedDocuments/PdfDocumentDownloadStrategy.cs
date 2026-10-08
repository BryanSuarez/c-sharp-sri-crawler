using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class PdfDocumentDownloadStrategy(ILogger<PdfDocumentDownloadStrategy> logger) : IDocumentDownloadStrategy
{
    public DownloadFormat Format => DownloadFormat.Pdf;

    public async Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document)
    {
        if (string.IsNullOrEmpty(document.PdfLinkId))
            return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "PDF download link is missing.");

        for (var attempt = 1; attempt <= SriRetryPolicy.DownloadAttempts; attempt++)
        {
            MemoryStream? content = null;
            try
            {
                var download = await session.Page.RunAndWaitForDownloadAsync(async () =>
                    await session.Page.EvaluateAsync(@"linkId => {
                        const form = document.getElementById('frmPrincipal');
                        if (typeof mojarra !== 'undefined') mojarra.jsfcljs(form, {[linkId]: linkId}, '');
                        else if (typeof PrimeFaces !== 'undefined') PrimeFaces.ab({ s: linkId, u: linkId });
                        else document.getElementById(linkId)?.click();
                    }", document.PdfLinkId), new() { Timeout = 40_000 });
                await using var source = await download.CreateReadStreamAsync();
                content = new MemoryStream();
                await source.CopyToAsync(content);
                content.Position = 0;
                var header = new byte[4];
                var read = await content.ReadAsync(header);
                content.Position = 0;
                if (read == 4 && header.AsSpan().SequenceEqual("%PDF"u8))
                    return OperationResult<DocumentContent>.Success(new(content, Format));
                await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "PDF download attempt {Attempt} failed.", attempt);
                await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
            }
            if (content is not null) await content.DisposeAsync();
        }
        return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "PDF download attempts were exhausted.");
    }
}
