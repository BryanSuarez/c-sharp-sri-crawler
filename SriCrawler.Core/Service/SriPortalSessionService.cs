using DescagaCompronanteSRI.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services;

public sealed class SriPortalSessionService(ILogger<SriPortalSessionService>? logger = null) : ISriPortalSessionService
{
    private readonly ILogger log = logger ?? NullLogger<SriPortalSessionService>.Instance;
    public async Task<string> GetPortalBodyAsync(
        PlaywrightSession session,
        string primaryUrl,
        string fallbackUrl,
        string logPrefix = "[Portal]")
    {
        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalPrimary");
        var body = await GetPortalBodyFromUrlAsync(session, primaryUrl, logPrefix);

        if (string.IsNullOrEmpty(body))
        {
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalFallback");
            body = await GetPortalBodyFromUrlAsync(session, fallbackUrl, logPrefix);
        }

        return body;
    }

    public async Task SubmitJSecurityCheckAsync(PlaywrightSession session, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalFormSubmitted");
        await session.Page.EvaluateAsync(@"async b => {
            await fetch(
                'https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet' +
                '/pages/consultas/recibidos/j_security_check',
                { method:'POST', body: b, credentials:'include',
                  headers:{'Content-Type':'application/x-www-form-urlencoded'} });
        }", body);
    }

    private async Task<string> GetPortalBodyFromUrlAsync(
        PlaywrightSession session,
        string url,
        string logPrefix)
    {
        try
        {
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalProgress");
            await session.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalProgress");
            await session.CerrarModalAsync();

            var body = await session.Page.EvaluateAsync<string>(@"() => {
                const p = new URLSearchParams();
                document.querySelectorAll('#form1 input').forEach(i => { if (i.name) p.append(i.name, i.value); });
                return p.toString();
            }");

            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "portalBodyRead");
            return body.Length > 10 ? body : "";
        }
        catch (Exception ex)
        {
            ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.BrowserEvent, code: "portalBodyFailed", errorType: ex.GetType().Name);
            return "";
        }
    }
}
