using System.Text;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class XmlDocumentDownloadStrategy(
    IDocumentParser parser, ILogger<XmlDocumentDownloadStrategy> logger) : IDocumentDownloadStrategy
{
    public DownloadFormat Format => DownloadFormat.Xml;

    public async Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document)
    {
        if (string.IsNullOrEmpty(document.XmlLinkId))
            return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "XML download link is missing.");

        for (var attempt = 1; attempt <= SriRetryPolicy.DownloadAttempts; attempt++)
        {
            try
            {
                var response = await session.Page.EvaluateAsync<string>(@"async ([linkId, url]) => {
                    const viewState = document.querySelector(""[name='javax.faces.ViewState']"")?.value;
                    if (!viewState) return '';
                    const response = await fetch(url, {
                        method: 'POST', credentials: 'include',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                        body: new URLSearchParams({ 'frmPrincipal':'frmPrincipal',
                            'javax.faces.ViewState': viewState, [linkId]: linkId }).toString()
                    });
                    return response.ok ? response.text() : '';
                }", new[] { document.XmlLinkId, ReceivedDocumentsPage.Url });
                if (string.IsNullOrWhiteSpace(response))
                {
                    await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
                    continue;
                }
                var extracted = parser.ExtractXml(response);
                if (!extracted.IsSuccess)
                {
                    await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
                    continue;
                }
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(extracted.Value!)).ToArray();
                return OperationResult<DocumentContent>.Success(new(new MemoryStream(bytes), Format));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "XML download attempt {Attempt} failed.", attempt);
            }
        }
        return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "XML download attempts were exhausted.");
    }
}
