using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Services.Parsing;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services
{
    /// <summary>
    /// Consulta y descarga comprobantes EMITIDOS del SRI.
    /// Cada llamada abre su propia PlaywrightSession → sin bloqueos concurrentes.
    /// PDF nombrado con la clave de acceso (49 dígitos).
    /// </summary>
    public class ConsultaComprobantesEmitidosService : IIssuedDocumentsService
    {
        private readonly IPlaywrightSessionFactory _sessionFactory;
        private readonly ISriLoginService _loginService;
        private readonly ISriPortalSessionService _portalSessionService;
        private readonly IPdfDownloadService _pdfDownloadService;

        private const string BASE_URL = "https://srienlinea.sri.gob.ec";
        private const string MENU_JSF = "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/menu.jsf";
        private const string JSF_EMITIDOS = "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recuperarComprobantes.jsf";

        public ConsultaComprobantesEmitidosService(
            IPlaywrightSessionFactory sessionFactory,
            ISriLoginService loginService,
            ISriPortalSessionService portalSessionService,
            IPdfDownloadService pdfDownloadService)
        {
            _sessionFactory = sessionFactory;
            _loginService = loginService;
            _portalSessionService = portalSessionService;
            _pdfDownloadService = pdfDownloadService;
        }

        // ════════════════════════════════════════════════════════════════════════
        //   ENTRADA
        // ════════════════════════════════════════════════════════════════════════

        public Task<SriIssuedUserResponse?> QueryAsync(IssuedDocumentsQuery query, string destination) =>
            ConsultarAsync(query, destination);

        public async Task<SriIssuedUserResponse?> ConsultarAsync(IssuedDocumentsQuery dto, string destino)
        {
            Console.WriteLine("[EMIT] Iniciando ConsultarAsync...");
            await using var s = await _sessionFactory.CreateAsync();

            Console.WriteLine("[EMIT] PASO 1: Login...");
            var profile = await _loginService.LoginAsync(s, dto.Usuario, dto.Password, dto.UsuarioAdicional);
            if (profile is null) { Console.WriteLine("[EMIT] ✗ Login fallido."); return null; }

            var usuario = new SriIssuedUserResponse
            {
                Ruc = profile.TaxpayerId,
                RazonSocial = profile.BusinessName
            };
            Console.WriteLine($"[EMIT] ✓ Login OK — RUC: {usuario.Ruc}");

            Console.WriteLine("[EMIT] PASO 2: Sesión JSF emitidos...");
            string viewState = await SesionJsfEmitidosAsync(s);
            if (string.IsNullOrEmpty(viewState))
            { Console.WriteLine("[EMIT] ✗ Sesión JSF fallida."); return null; }
            Console.WriteLine($"[EMIT] ✓ Formulario listo. ViewState={viewState.Length} chars.");

            Console.WriteLine("[EMIT] PASO 3: Consultando tabla...");
            int filas = await ConsultarTablaAsync(s, dto, viewState);
            if (filas < 0) { Console.WriteLine("[EMIT] ✗ Error en consulta."); return null; }
            if (filas == 0) { Console.WriteLine("[EMIT] Sin comprobantes."); usuario.TotalComprobantes = 0; return usuario; }
            Console.WriteLine($"[EMIT] ✓ {filas} comprobante(s).");

            Console.WriteLine("[EMIT] PASO 4: Descargando PDFs...");
            Directory.CreateDirectory(destino);
            await DescargarAsync(s, filas, destino, usuario);

            usuario.TotalComprobantes = filas;
            Console.WriteLine($"[EMIT] ✓ Fin. {usuario.Comprobantes.Count}/{filas} descargados.");
            return usuario;
        }

        // ════════════════════════════════════════════════════════════════════════
        //   PASO 2: Sesión JSF → formulario emitidos visible
        // ════════════════════════════════════════════════════════════════════════

        private async Task<string> SesionJsfEmitidosAsync(PlaywrightSession s)
        {
            string body = await _portalSessionService.GetPortalBodyAsync(
                s,
                $"{BASE_URL}/tuportal-internet/accederAplicacion.jspa?redireccion=60&idGrupo=58",
                $"{BASE_URL}/tuportal-internet/menusFavoritos.jspa?redireccion=60&idGrupo=58");

            if (!string.IsNullOrEmpty(body))
            {
                await _portalSessionService.SubmitJSecurityCheckAsync(s, body);
            }

            for (int intento = 1; intento <= 2; intento++)
            {
                try
                {
                    Console.WriteLine($"[JSF] Cargando menu.jsf (intento {intento})...");
                    await s.Page.GotoAsync(MENU_JSF,
                        new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 25_000 });
                    await s.CerrarModalAsync();

                    await s.Page.WaitForSelectorAsync(
                        ".sri-listaLinks a, #consultaDocumentoForm a",
                        new() { Timeout = 15_000, State = WaitForSelectorState.Visible });

                    Console.WriteLine("[JSF] Click en link emitidos...");
                    if (!await ClickLinkEmitidosAsync(s)) { await Task.Delay(2_000); continue; }

                    Console.WriteLine("[JSF] Esperando formulario...");
                    await s.Page.WaitForSelectorAsync(
                        "#frmPrincipal\\:calendarFechaDesde_input",
                        new() { Timeout = 30_000, State = WaitForSelectorState.Visible });
                    await s.CerrarModalAsync();

                    string vs = await s.Page.EvaluateAsync<string>(
                        "() => document.querySelector('[name=\"javax.faces.ViewState\"]')?.value ?? ''");
                    if (vs.Length >= 10)
                    { Console.WriteLine($"[JSF] ✓ ViewState OK ({vs.Length} chars)."); return vs; }

                    Console.WriteLine($"[JSF] ✗ ViewState inválido ({vs.Length}).");
                }
                catch (Exception ex)
                { Console.WriteLine($"[JSF] ✗ Intento {intento}: {ex.Message}"); await Task.Delay(2_000); }
            }
            return string.Empty;
        }

        private async Task<bool> ClickLinkEmitidosAsync(PlaywrightSession s)
        {
            for (int t = 1; t <= SriRetryPolicy.DownloadAttempts; t++)
            {
                try
                {
                    bool existe = await s.Page.EvaluateAsync<bool>(@"() => {
                        for (const a of document.querySelectorAll('.sri-listaLinks a, #consultaDocumentoForm a'))
                            if (a.textContent.trim().toLowerCase().includes('comprobantes electr')) return true;
                        return false;
                    }");
                    if (!existe)
                    {
                        Console.WriteLine($"[Click] ✗ Link no encontrado (intento {t}).");
                        await Task.Delay(SriRetryPolicy.QueryBackoff(1));
                        if (t < SriRetryPolicy.DownloadAttempts) await s.Page.GotoAsync(MENU_JSF,
                            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20_000 });
                        continue;
                    }
                    try
                    {
                        var loc = s.Page.Locator(".sri-listaLinks a, #consultaDocumentoForm a")
                                        .Filter(new() { HasText = "Comprobantes electr" }).First;
                        await loc.ScrollIntoViewIfNeededAsync();
                        await loc.ClickAsync(new() { Timeout = 8_000 });
                        Console.WriteLine("[Click] ✓ Click Playwright OK.");
                        return true;
                    }
                    catch
                    {
                        await s.Page.EvaluateAsync(@"() => {
                            for (const a of document.querySelectorAll('.sri-listaLinks a, #consultaDocumentoForm a'))
                                if (a.textContent.trim().toLowerCase().includes('comprobantes electr'))
                                { a.click(); return; }
                        }");
                        Console.WriteLine("[Click] ✓ Click JS OK.");
                        return true;
                    }
                }
                catch (Exception ex)
                { Console.WriteLine($"[Click] ✗ Intento {t}: {ex.Message}"); await Task.Delay(SriRetryPolicy.QueryBackoff(1)); }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════════
        //   PASO 3: Llenar formulario + click Consultar
        // ════════════════════════════════════════════════════════════════════════

        private async Task<int> ConsultarTablaAsync(
            PlaywrightSession s, IssuedDocumentsQuery dto, string viewState)
        {
            DateTime fecha = new DateTime(dto.Anio, dto.Mes, dto.Dia);
            string fechaStr = fecha.ToString("dd/MM/yyyy");
            Console.WriteLine($"[Consulta] fecha={fechaStr} tipo={dto.Comprobante}");

            // Fecha — jQuery UI datepicker (hasDatepicker)
            await s.Page.EvaluateAsync<bool>(@"(fecha) => {
                try {
                    const inp = document.getElementById('frmPrincipal:calendarFechaDesde_input');
                    if (!inp) return false;
                    if (typeof $ !== 'undefined' && $(inp).datepicker) {
                        $(inp).datepicker('setDate', fecha);
                        inp.dispatchEvent(new Event('change', { bubbles: true }));
                        inp.dispatchEvent(new Event('blur',   { bubbles: true }));
                        return inp.value === fecha;
                    }
                    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                    setter.call(inp, fecha);
                    inp.dispatchEvent(new Event('input',  { bubbles: true }));
                    inp.dispatchEvent(new Event('change', { bubbles: true }));
                    inp.dispatchEvent(new Event('blur',   { bubbles: true }));
                    if (typeof $ !== 'undefined') $(inp).trigger('change').trigger('blur');
                    return inp.value === fecha;
                } catch(e) { return false; }
            }", fechaStr);

            string valorFecha = await s.Page.EvaluateAsync<string>(
                "() => document.getElementById('frmPrincipal:calendarFechaDesde_input')?.value ?? ''");
            Console.WriteLine($"[Consulta] Fecha en input: '{valorFecha}'");

            if (valorFecha != fechaStr)
            {
                Console.WriteLine("[Consulta] Fallback teclado...");
                var inpLoc = s.Page.Locator("#frmPrincipal\\:calendarFechaDesde_input");
                await inpLoc.ClickAsync(new() { ClickCount = 3 });
                await inpLoc.PressSequentiallyAsync(fechaStr, new() { Delay = 50 });
                await inpLoc.PressAsync("Tab");
                await s.Page.WaitForTimeoutAsync(300);
                valorFecha = await s.Page.EvaluateAsync<string>(
                    "() => document.getElementById('frmPrincipal:calendarFechaDesde_input')?.value ?? ''");
                Console.WriteLine($"[Consulta] Fecha tras teclado: '{valorFecha}'");
            }

            await s.Page.WaitForTimeoutAsync(200);
            await s.Page.SelectOptionAsync("#frmPrincipal\\:cmbEstadoAutorizacion", "AUT");
            await s.Page.SelectOptionAsync("#frmPrincipal\\:cmbTipoComprobante", dto.Comprobante.ToString());
            Console.WriteLine("[Consulta] ✓ Formulario llenado.");

            for (int intento = 1; intento <= SriRetryPolicy.QueryAttempts; intento++)
            {
                Console.WriteLine($"[Consulta] Intento {intento}/4...");
                try
                {
                    await s.CerrarModalAsync();
                    await s.Page.EvaluateAsync(@"() => {
                        const b = document.querySelector('#frmPrincipal\\:btnConsultar');
                        if (!b) return;
                        b.disabled = false;
                        b.removeAttribute('aria-disabled');
                        b.classList.remove('ui-state-disabled');
                        document.querySelector('.ui-messages-warn')?.remove();
                    }");

                    var responseTask = s.Page.WaitForResponseAsync(
                        r => r.Url.Contains("recuperarComprobantes.jsf"),
                        new() { Timeout = 60_000 });

                    var box = await s.Page.Locator("#frmPrincipal\\:btnConsultar").BoundingBoxAsync();
                    if (box is not null)
                        await s.Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                    else
                        await s.Page.EvaluateAsync(
                            "() => document.querySelector('#frmPrincipal\\:btnConsultar')?.click()");

                    var response = await responseTask;
                    string body = await response.TextAsync();
                    Console.WriteLine($"[Consulta] HTTP {response.Status} | {body.Length} chars.");

                    if (body.Contains("captcha", StringComparison.OrdinalIgnoreCase) &&
                        body.Contains("ui-messages-warn"))
                    {
                        Console.WriteLine($"[Consulta] ⚠ Captcha, esperando {SriRetryPolicy.CaptchaBackoff(intento).TotalSeconds:0}s...");
                        await Task.Delay(SriRetryPolicy.CaptchaBackoff(intento));
                        continue;
                    }

                    if (body.Contains("No existen datos", StringComparison.OrdinalIgnoreCase))
                    { Console.WriteLine("[Consulta] Sin datos."); return 0; }

                    try
                    {
                        await s.Page.WaitForFunctionAsync(@"() =>
                            document.querySelectorAll(
                                '#frmPrincipal\\:tablaCompEmitidos_data tr[data-ri]').length > 0
                            || !!document.querySelector('.ui-messages-warn-summary')",
                            null, new() { Timeout = 15_000, PollingInterval = 500 });
                    }
                    catch { /* ya puede estar */ }

                    string warn = await s.Page.EvaluateAsync<string>(
                        "() => document.querySelector('.ui-messages-warn-summary')?.textContent?.trim() ?? ''");

                    if (warn.Contains("No existen datos", StringComparison.OrdinalIgnoreCase))
                    { Console.WriteLine("[Consulta] Sin datos (DOM)."); return 0; }

                    int filas = await s.Page.EvaluateAsync<int>(@"() =>
                        document.querySelectorAll(
                            '#frmPrincipal\\:tablaCompEmitidos_data tr[data-ri]').length");

                    Console.WriteLine($"[Consulta] filas={filas} warn='{warn}'");
                    if (filas > 0) { Console.WriteLine($"[Consulta] ✓ {filas} filas."); return filas; }

                    Console.WriteLine("[Consulta] Sin filas, reintentando...");
                    await Task.Delay(SriRetryPolicy.QueryBackoff(intento));
                }
                catch (Exception ex)
                { Console.WriteLine($"[Consulta] ✗ Intento {intento}: {ex.Message}"); await Task.Delay(SriRetryPolicy.QueryBackoff(intento)); }
            }

            Console.WriteLine("[Consulta] ✗ Agotados los 4 intentos.");
            return -1;
        }

        // ════════════════════════════════════════════════════════════════════════
        //   PASO 4: Leer filas + descargar PDF
        // ════════════════════════════════════════════════════════════════════════

        private async Task DescargarAsync(
            PlaywrightSession s, int total, string dir, SriIssuedUserResponse usuario)
        {
            await s.Page.WaitForSelectorAsync(
                "#frmPrincipal\\:tablaCompEmitidos_data tr[data-ri]",
                new() { Timeout = 10_000 });

            for (int i = 0; i < total; i++)
            {
                Console.WriteLine($"[Descarga] {i + 1}/{total}...");
                var comp = await LeerFilaAsync(s, i, total);
                if (comp is null) { Console.WriteLine($"[Descarga] ✗ Fila {i} nula."); continue; }

                // Nombre = clave de acceso (49 dígitos) — siempre única
                string ruta = PathService.CombineSafe(
                    dir,
                    !string.IsNullOrWhiteSpace(comp.ClaveAcceso) ? comp.ClaveAcceso : comp.NumeroFactura,
                    ".pdf",
                    "sin_clave");

                bool ok = await PdfAsync(s, comp, ruta);
                if (ok) { usuario.Comprobantes.Add(comp); Console.WriteLine($"[Descarga] ✓ {Path.GetFileName(ruta)}"); }
                else Console.WriteLine($"[Descarga] ✗ Falló: {comp.ClaveAcceso}");

                await s.Page.WaitForTimeoutAsync(400);
            }
        }

        /// <summary>
        /// Lee todas las columnas de una fila. Estructura REAL de la tabla:
        ///  td[0] = Nro (número de fila)
        ///  td[1] = Tipo y serie          → "Factura 001-001-000000011"
        ///  td[2] = Clave de acceso       → link j_idt53, texto = 49 dígitos
        ///  td[3] = Fecha/hora autorización → "15/01/2026 21:38:04"
        ///  td[4] = Fecha emisión          → "15/01/2026"
        ///  td[5] = Valor sin impuestos    → "661.56"
        ///  td[6] = IVA                   → "99.23"
        ///  td[7] = Importe total          → "760.79"
        ///  td[8] = RIDE (lnkPdf)         → mojarra.jsfcljs → descarga PDF
        ///  td[9] = Documentos relacionados → link j_idt78
        /// </summary>
        private async Task<IssuedDocumentResponse?> LeerFilaAsync(PlaywrightSession s, int idx, int total)
        {
            try
            {
                var rows = await s.Page.QuerySelectorAllAsync(
                    "#frmPrincipal\\:tablaCompEmitidos_data tr[data-ri]");

                if (idx >= rows.Count)
                { Console.WriteLine($"[Fila] ✗ idx={idx} >= DOM={rows.Count}"); return null; }

                var rowHtml = await rows[idx].InnerHTMLAsync();
                var comp = IssuedDocumentsTableParser.ParseRow(rowHtml);
                if (comp is null)
                { Console.WriteLine($"[Fila] ✗ No se pudo parsear fila {idx}"); return null; }

                Console.WriteLine($"[Fila] [{idx + 1}/{total}] " +
                    $"CA={comp.ClaveAcceso[..Math.Min(20, comp.ClaveAcceso.Length)]}... " +
                    $"| {comp.NumeroFactura} | Total={comp.ImporteTotal} | PDF={comp.PdfLinkId}");

                await rows[idx].EvaluateAsync("el => el.scrollIntoView({ block:'center' })");
                return comp;
            }
            catch (Exception ex)
            { Console.WriteLine($"[Fila] ✗ Error idx={idx}: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Descarga el PDF haciendo click en lnkPdf.
        /// onclick: mojarra.jsfcljs(document.getElementById('frmPrincipal'),
        ///          {'frmPrincipal:tablaCompEmitidos:N:lnkPdf':'...'}, '')
        /// </summary>
        private async Task<bool> PdfAsync(PlaywrightSession s, IssuedDocumentResponse comp, string ruta)
        {
            var ok = await _pdfDownloadService.DownloadAsync(s, comp.PdfLinkId, ruta);
            if (ok)
            {
                comp.RutaArchivo = ruta;
            }

            return ok;
        }
    }
}