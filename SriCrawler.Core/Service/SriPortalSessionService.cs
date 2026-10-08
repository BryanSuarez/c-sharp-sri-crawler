using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services;

public sealed class SriPortalSessionService : ISriPortalSessionService
{
    public async Task<string> GetPortalBodyAsync(
        PlaywrightSession session,
        string primaryUrl,
        string fallbackUrl,
        string logPrefix = "[Portal]")
    {
        Console.WriteLine($"{logPrefix} Intentando URL primaria...");
        var body = await GetPortalBodyFromUrlAsync(session, primaryUrl, logPrefix);

        if (string.IsNullOrEmpty(body))
        {
            Console.WriteLine($"{logPrefix} Sin body en URL primaria, probando alternativa...");
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

        Console.WriteLine("[JSF] Enviando j_security_check...");
        await session.Page.EvaluateAsync(@"async b => {
            await fetch(
                'https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet' +
                '/pages/consultas/recibidos/j_security_check',
                { method:'POST', body: b, credentials:'include',
                  headers:{'Content-Type':'application/x-www-form-urlencoded'} });
        }", body);
    }

    private static async Task<string> GetPortalBodyFromUrlAsync(
        PlaywrightSession session,
        string url,
        string logPrefix)
    {
        try
        {
            Console.WriteLine($"{logPrefix} GotoAsync: {url}");
            await session.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
            Console.WriteLine($"{logPrefix} URL resultante: {session.Page.Url}");
            await session.CerrarModalAsync();

            var body = await session.Page.EvaluateAsync<string>(@"() => {
                const p = new URLSearchParams();
                document.querySelectorAll('#form1 input').forEach(i => { if (i.name) p.append(i.name, i.value); });
                return p.toString();
            }");

            Console.WriteLine($"{logPrefix} Body obtenido: {body.Length} chars.");
            return body.Length > 10 ? body : "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{logPrefix} ✗ Error obteniendo body portal: {ex.Message}");
            return "";
        }
    }
}
