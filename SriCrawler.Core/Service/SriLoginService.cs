using DescagaCompronanteSRI.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using Microsoft.Playwright;
using Microsoft.AspNetCore.WebUtilities;

namespace DescagaCompronanteSRI.Services;

public sealed class SriLoginService(ILogger<SriLoginService>? logger = null) : ISriLoginService
{
    private readonly ILogger log = logger ?? NullLogger<SriLoginService>.Instance;
    private const string BaseUrl = "https://srienlinea.sri.gob.ec";

    public async Task<SriUserProfile?> LoginAsync(
        PlaywrightSession session,
        string user,
        string password,
        string? additionalUser)
    {
        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        await session.Page.GotoAsync(
            BaseUrl + "/auth/realms/Internet/protocol/openid-connect/auth" +
            "?client_id=app-sri-claves-angular" +
            "&redirect_uri=https%3A%2F%2Fsrienlinea.sri.gob.ec%2Fsri-en-linea%2F%2Fcontribuyente%2Fperfil" +
            "&response_mode=fragment&response_type=code&scope=openid",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        await session.Page.WaitForSelectorAsync("#usuario", new() { Timeout = 20_000 });
        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        await session.Page.EvaluateAsync(@"([u, p]) => {
            const set = (sel, val) => {
                const el = document.querySelector(sel);
                const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                setter.call(el, val);
                el.dispatchEvent(new Event('input',  { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            };
            set('#usuario',  u);
            set('#password', p);
        }", new[] { user.ToUpperInvariant(), password });

        if (!string.IsNullOrWhiteSpace(additionalUser))
        {
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
            await session.Page.EvaluateAsync(
                "ci => { const el = document.querySelector('#ciAdicional'); if (el) el.value = ci; }",
                additionalUser);
        }
        else
        {
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        }

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        await session.Page.ClickAsync("#kc-login");

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");
        try
        {
            await session.Page.WaitForSelectorAsync("#sri-menu, .kc-feedback-text", new() { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            if (IsAuthorizationRedirect(session.Page.Url))
            {
                // Keycloak accepted the credentials, but the Angular profile has not rendered.
                // ReceivedDocumentsPage still verifies access to the protected application.
                ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.ProfileUnavailable, code: "profileUnavailable");
                return new SriUserProfile { TaxpayerId = user };
            }
            ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.BrowserEvent, code: "loginResponseTimeout");
            throw;
        }
        // Authentication redirects can contain temporary authorization codes.
        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "loginProgress");

        if (await session.Page.QuerySelectorAsync(".kc-feedback-text") is { } err)
        {
            ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.BrowserEvent, code: "credentialsRejected");
            return null;
        }

        ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "authenticationAccepted");
        await session.CerrarModalAsync();
        return await ReadProfileAsync(session);
    }

    internal static bool IsAuthorizationRedirect(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "srienlinea.sri.gob.ec" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            System.Text.RegularExpressions.Regex.Replace(uri.AbsolutePath, "/+", "/") != "/sri-en-linea/contribuyente/perfil")
            return false;
        var fragment = QueryHelpers.ParseQuery(uri.Fragment.TrimStart('#'));
        return !fragment.ContainsKey("error") && fragment.TryGetValue("code", out var code) &&
            !string.IsNullOrWhiteSpace(code.ToString());
    }

    private Task<SriUserProfile> ReadProfileAsync(PlaywrightSession session) =>
        ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.ProfileRead, () => ReadProfileCoreAsync(session),
            p => string.IsNullOrWhiteSpace(p.BusinessName) ? DiagnosticOutcome.Failed : DiagnosticOutcome.Succeeded);

    private async Task<SriUserProfile> ReadProfileCoreAsync(PlaywrightSession session)
    {
        var profile = new SriUserProfile();
        try
        {
            ExtractionDiagnostics.Event(log, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "profileReading");
            await session.Page.GotoAsync(
                BaseUrl + "/sri-en-linea/contribuyente/perfil",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 12_000 });

            profile.TaxpayerId = await session.Page.EvaluateAsync<string>(
                "() => document.querySelector('.area-usuario-blue span')?.textContent?.trim() ?? ''");
            profile.BusinessName = await session.Page.EvaluateAsync<string>(
                "() => document.querySelector('#id_nombre_razon_social')?.textContent?.trim() ?? ''");

            if (string.IsNullOrWhiteSpace(profile.BusinessName) || string.IsNullOrWhiteSpace(profile.TaxpayerId))
                ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.ProfileUnavailable, code: "profileIncomplete");
        }
        catch (Exception ex)
        {
            ExtractionDiagnostics.Event(log, LogLevel.Warning, DiagnosticEvent.BrowserEvent, code: "profileReadFailed", errorType: ex.GetType().Name);
        }

        return profile;
    }
}
