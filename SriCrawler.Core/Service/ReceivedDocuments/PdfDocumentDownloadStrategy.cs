using DescagaCompronanteSRI.Contracts;
using Microsoft.Extensions.Options;
using DescagaCompronanteSRI.Validation;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class PdfDocumentDownloadStrategy(ILogger<PdfDocumentDownloadStrategy> logger, IOptions<DocumentValidationOptions>? options = null) : IDocumentDownloadStrategy
{
    public DownloadFormat Format => DownloadFormat.Pdf;

    public async Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document, CancellationToken token = default)
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
                    }", document.PdfLinkId), new() { Timeout = 40_000 }).WaitAsync(token);
                await using var source = await download.CreateReadStreamAsync();
                content = new MemoryStream();
                await BoundedDocumentStream.CopyAsync(source, content, options?.Value.MaxPdfBytes ?? 20 * 1024 * 1024, token);
                content.Position = 0;
                var completed = content;
                content = null;
                return OperationResult<DocumentContent>.Success(new(completed, Format));
            }
            catch (DocumentSizeLimitException)
            {
                return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.InputTooLarge, "PDF exceeds the configured size limit.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "PDF download attempt {Attempt} failed.", attempt);
                await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt), token);
            }
            finally { if (content is not null) await content.DisposeAsync(); }
        }
        return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "PDF download attempts were exhausted.");
    }
}
