using DescagaCompronanteSRI.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DescagaCompronanteSRI.Models.Enums;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Jobs;

public sealed class HttpSriDocumentJsonParser(HttpClient client, IOptions<ExtractionJobOptions> options, ILogger<HttpSriDocumentJsonParser>? logger = null) : ISriDocumentJsonParser
{
    public const string ParserName = "taxo-sri-xml-2-json";
    public const string ParserVersion = "1.8.0";
    private readonly ILogger log = logger ?? NullLogger<HttpSriDocumentJsonParser>.Instance;
    public async Task<JsonConversion> ParseAsync(string xml, DocumentType type, string accessKey, CancellationToken token)
    {
        var result = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.JsonParsing, () => ParseCoreAsync(xml, type, accessKey, token),
            value => value.Status == DocumentParseStatus.Failed ? DiagnosticOutcome.Failed : value.Status == DocumentParseStatus.Unsupported ? DiagnosticOutcome.Unsupported : DiagnosticOutcome.Succeeded);
        if (result.ErrorCode is not null) ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.DocumentIssue, code: result.ErrorCode,
            operation: DiagnosticOperation.JsonParsing);
        return result;
    }
    private async Task<JsonConversion> ParseCoreAsync(string xml, DocumentType type, string accessKey, CancellationToken token)
    {
        if (type is DocumentType.RemissionGuide or DocumentType.RemissionGuideAlternative)
            return new(DocumentParseStatus.Unsupported, ErrorCode: "unsupportedDocumentType");
        if (System.Text.Encoding.UTF8.GetByteCount(xml) > 20 * 1024 * 1024)
            return new(DocumentParseStatus.Failed, ErrorCode: "inputTooLarge");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ParserTimeoutSeconds));
            try
            {
                var requestId = Guid.NewGuid();
                using var requestScope = ExtractionDiagnostics.SafeScope(log, new Dictionary<string, object> { ["requestId"] = requestId, ["transportAttempt"] = attempt + 1 });
                ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.ParserRequest, code: "parserRequestStarted");
                using var response = await client.PostAsJsonAsync("parse", new { requestId, xml }, timeout.Token);
                ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.ParserRequest, code: "parserResponseReceived", detail: (int)response.StatusCode);
                if ((int)response.StatusCode >= 500)
                {
                    if (attempt == 0) { ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: "parserServerRetry", detail: 2); await Task.Delay(2000, token); continue; }
                    return new(DocumentParseStatus.Failed, ErrorCode: "parserUnavailable");
                }
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                    return new(DocumentParseStatus.Unsupported, ErrorCode: "unsupportedDocumentType");
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                    return new(DocumentParseStatus.Failed, ErrorCode: "inputTooLarge");
                if (!response.IsSuccessStatusCode)
                    return new(DocumentParseStatus.Failed, ErrorCode: "invalidXml");
                var parsed = await response.Content.ReadFromJsonAsync<ParserResponse>(ExtractionJobs.Json, timeout.Token);
                var json = parsed?.DocumentJson;
                var expectedType = type switch { DocumentType.Invoice => "factura", DocumentType.PurchaseSettlement => "liquidacionCompra",
                    DocumentType.CreditNote => "notaCredito", DocumentType.DebitNote => "notaDebito", DocumentType.Withholding => "comprobanteRetencion", _ => "" };
                if (parsed?.Status != "parsed" || parsed.ParserName != ParserName || parsed.ParserVersion != ParserVersion)
                    return new(DocumentParseStatus.Failed, ErrorCode: "invalidParserResponse");
                if (parsed.DocumentType != expectedType || json is null ||
                    !json.Value.TryGetProperty("infoTributaria", out var tax) ||
                    !tax.TryGetProperty("claveAcceso", out var key) || key.ValueKind != JsonValueKind.String || key.GetString() != accessKey)
                    return new(DocumentParseStatus.Failed, ErrorCode: "documentIdentityMismatch");
                return new(DocumentParseStatus.Parsed, json, parsed.ParserName, parsed.ParserVersion);
            }
            catch (Exception e) when ((e is HttpRequestException or OperationCanceledException) && !token.IsCancellationRequested)
            {
                if (attempt == 0) { ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: "parserTransportRetry", errorType: e.GetType().Name, detail: 2); await Task.Delay(2000, token); continue; }
            }
            catch (InvalidOperationException) { return new(DocumentParseStatus.Failed, ErrorCode: "invalidParserResponse"); }
            catch (JsonException) { return new(DocumentParseStatus.Failed, ErrorCode: "invalidParserResponse"); }
        }
        return new(DocumentParseStatus.Failed, ErrorCode: "parserUnavailable");
    }
    private sealed record ParserResponse(JsonElement DocumentJson, string ParserName, string ParserVersion, string DocumentType, string Status);
}
