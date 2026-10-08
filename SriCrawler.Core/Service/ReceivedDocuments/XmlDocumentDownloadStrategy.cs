using DescagaCompronanteSRI.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using DescagaCompronanteSRI.Validation;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class XmlDocumentDownloadStrategy(
    IDocumentParser parser, ILogger<XmlDocumentDownloadStrategy> logger, IOptions<DocumentValidationOptions>? options = null) : IDocumentDownloadStrategy
{
    public DownloadFormat Format => DownloadFormat.Xml;

    public async Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(document.XmlLinkId))
            return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "XML download link is missing.");

        for (var attempt = 1; attempt <= SriRetryPolicy.DownloadAttempts; attempt++)
        {
            try
            {
                var response = await session.Page.EvaluateAsync<string>(@"async ([linkId, url, maxBytes]) => {
                    const viewState = document.querySelector(""[name='javax.faces.ViewState']"")?.value;
                    if (!viewState) return '';
                    const response = await fetch(url, {
                        method: 'POST', credentials: 'include',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                        body: new URLSearchParams({ 'frmPrincipal':'frmPrincipal',
                            'javax.faces.ViewState': viewState, [linkId]: linkId }).toString()
                    });
                    if (!response.ok) return '';
                    const reader = response.body.getReader();
                    const chunks = []; let size = 0;
                    while (true) {
                        const {done, value} = await reader.read();
                        if (done) break;
                        size += value.length;
                        if (size > Number(maxBytes)) { await reader.cancel(); return '__INPUT_TOO_LARGE__'; }
                        chunks.push(value);
                    }
                    const data = new Uint8Array(size); let offset = 0;
                    for (const chunk of chunks) { data.set(chunk, offset); offset += chunk.length; }
                    try { return new TextDecoder('utf-8', {fatal:true}).decode(data); }
                    catch { return '__INVALID_XML_ENCODING__'; }
                }", new[] { document.XmlLinkId, ReceivedDocumentsPage.Url, (options?.Value.MaxXmlBytes ?? 20 * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) }).WaitAsync(token);
                if (response == "__INPUT_TOO_LARGE__")
                    return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.InputTooLarge, "XML exceeds the configured size limit.");
                if (response == "__INVALID_XML_ENCODING__")
                    return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.InvalidDocument, "XML encoding is invalid.");
                if (string.IsNullOrWhiteSpace(response))
                {
                    ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: "emptyDownloadResponse", detail: attempt);
                    await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt), token);
                    continue;
                }
                var extracted = parser.ExtractXml(response);
                if (!extracted.IsSuccess)
                {
                    return new(default, extracted.Error);
                }
                var bytes = Encoding.UTF8.GetBytes(extracted.Value!);
                return OperationResult<DocumentContent>.Success(new(new MemoryStream(bytes), Format, response));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: "xmlDownloadRetry", errorType: exception.GetType().Name, detail: attempt);
            }
        }
        return OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "XML download attempts were exhausted.");
    }
}
