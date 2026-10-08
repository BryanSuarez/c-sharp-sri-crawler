using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using Microsoft.Playwright;
using Microsoft.AspNetCore.WebUtilities;

namespace DescagaCompronanteSRI.Services;

public sealed class SriLoginService : ISriLoginService
{
    private const string BaseUrl = "https://srienlinea.sri.gob.ec";

    public async Task<SriUserProfile?> LoginAsync(
        PlaywrightSession session,
        string user,
        string password,
        string? additionalUser)
    {
        Console.WriteLine("[Login] Navegando a página de login...");
        await session.Page.GotoAsync(
            BaseUrl + "/auth/realms/Internet/protocol/openid-connect/auth" +
            "?client_id=app-sri-claves-angular" +
            "&redirect_uri=https%3A%2F%2Fsrienlinea.sri.gob.ec%2Fsri-en-linea%2F%2Fcontribuyente%2Fperfil" +
            "&response_mode=fragment&response_type=code&scope=openid",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        Console.WriteLine("[Login] Esperando campo #usuario...");
        await session.Page.WaitForSelectorAsync("#usuario", new() { Timeout = 20_000 });
        Console.WriteLine("[Login] ✓ Formulario visible.");

        Console.WriteLine($"[Login] Ingresando credenciales — usuario: {user.ToUpperInvariant()} | password.Length: {password?.Length ?? 0}");
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
            Console.WriteLine($"[Login] Ingresando usuarioAdicional: {additionalUser}");
            await session.Page.EvaluateAsync(
                "ci => { const el = document.querySelector('#ciAdicional'); if (el) el.value = ci; }",
                additionalUser);
        }
        else
        {
            Console.WriteLine("[Login] usuarioAdicional vacío, omitiendo.");
        }

        Console.WriteLine("[Login] Haciendo clic en #kc-login...");
        await session.Page.ClickAsync("#kc-login");

        Console.WriteLine("[Login] Esperando respuesta (#sri-menu o .kc-feedback-text)...");
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
                Console.WriteLine("[Login] Trusted authorization redirect received; profile menu was not rendered. Application access will be verified separately.");
                return new SriUserProfile { TaxpayerId = user };
            }
            var evidence = await session.Page.EvaluateAsync<string>("""
                () => JSON.stringify({
                    path: location.pathname,
                    menuPresent: !!document.querySelector('#sri-menu'),
                    loginFormPresent: !!document.querySelector('#usuario'),
                    authorizationCodePresent: new URLSearchParams(location.hash.slice(1)).has('code'),
                    authorizationErrorPresent: new URLSearchParams(location.hash.slice(1)).has('error'),
                    feedback: [...document.querySelectorAll('.kc-feedback-text, .alert-error, .alert-warning')]
                        .map(e => e.textContent.trim()).join(' ').slice(0, 300)
                })
                """);
            Console.WriteLine($"[Login] Response timeout evidence: {evidence}");
            throw;
        }
        // Authentication redirects can contain temporary authorization codes.
        Console.WriteLine($"[Login] URL tras login: {new Uri(session.Page.Url).GetLeftPart(UriPartial.Path)}");

        if (await session.Page.QuerySelectorAsync(".kc-feedback-text") is { } err)
        {
            Console.WriteLine($"[Login] ✗ Error credenciales: {(await err.InnerTextAsync()).Trim()}");
            return null;
        }

        Console.WriteLine("[Login] ✓ Login exitoso.");
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

    private static async Task<SriUserProfile> ReadProfileAsync(PlaywrightSession session)
    {
        var profile = new SriUserProfile();
        try
        {
            Console.WriteLine("[Perfil] Navegando a perfil...");
            await session.Page.GotoAsync(
                BaseUrl + "/sri-en-linea/contribuyente/perfil",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 12_000 });

            profile.TaxpayerId = await session.Page.EvaluateAsync<string>(
                "() => document.querySelector('.area-usuario-blue span')?.textContent?.trim() ?? ''");
            profile.BusinessName = await session.Page.EvaluateAsync<string>(
                "() => document.querySelector('#id_nombre_razon_social')?.textContent?.trim() ?? ''");

            Console.WriteLine($"[Perfil] ✓ RUC: {profile.TaxpayerId} | {profile.BusinessName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Perfil] ℹ No crítico: {ex.Message}");
        }

        return profile;
    }
}
