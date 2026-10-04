using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Services.Parsing;
using Microsoft.Playwright;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DescagaCompronanteSRI.Services
{
    public class ConsultaComprobantesService : IReceivedDocumentsService
    {
        private readonly IPlaywrightSessionFactory _sessionFactory;
        private readonly ISriLoginService _loginService;
        private readonly ISriPortalSessionService _portalSessionService;
        private readonly IPdfDownloadService _pdfDownloadService;

        private const string JSF_URL =
            "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet" +
            "/pages/consultas/recibidos/comprobantesRecibidos.jsf";

        public ConsultaComprobantesService(
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

        public Task<SriUserResponse?> QueryAsync(ReceivedDocumentsQuery query, string destination) =>
            ConsultarAsync(query, destination);

        // ── Entrada ──────────────────────────────────────────────────────────────

        public async Task<SriUserResponse?> ConsultarAsync(ReceivedDocumentsQuery dto, string destino)
        {
            Console.WriteLine("[SRI] Iniciando ConsultarAsync...");
            await using var s = await _sessionFactory.CreateAsync();

            Console.WriteLine("[SRI] PASO 1: Login...");
            var profile = await _loginService.LoginAsync(s, dto.Usuario, dto.Password, dto.UsuarioAdicional);
            if (profile is null)
            {
                Console.WriteLine("[SRI] ✗ Login fallido.");
                return null;
            }

            var usuario = new SriUserResponse
            {
                Ruc = profile.TaxpayerId,
                RazonSocial = profile.BusinessName
            };
            Console.WriteLine($"[SRI] ✓ Login OK — RUC: {usuario.Ruc}");

            Console.WriteLine("[SRI] PASO 2: Sesión JSF...");
            if (!await SesionJsfAsync(s))
            {
                Console.WriteLine("[SRI] ✗ Sesión JSF fallida.");
                return null;
            }
            Console.WriteLine("[SRI] ✓ Sesión JSF OK.");

            Console.WriteLine("[SRI] PASO 3: Consultar tabla...");
            int filas = await ConsultarAsync(s, dto);
            if (filas <= 0)
            {
                Console.WriteLine($"[SRI] Resultado filas={filas} (0=sin datos, -1=error).");
                return filas == 0 ? usuario : null;
            }
            Console.WriteLine($"[SRI] ✓ {filas} comprobante(s) encontrados.");

            Console.WriteLine("[SRI] PASO 4: Descargar...");
            Directory.CreateDirectory(destino);
            await DescargarAsync(s, dto, destino, filas, usuario);

            usuario.TotalComprobantes = filas;
            Console.WriteLine($"[SRI] ✓ Fin. {usuario.Comprobantes.Count}/{filas} descargados.");
            return usuario;
        }

        // ── Paso 2: Sesión JSF ────────────────────────────────────────────────────

        private async Task<bool> SesionJsfAsync(PlaywrightSession s)
        {
            string body = await _portalSessionService.GetPortalBodyAsync(
                s,
                "https://srienlinea.sri.gob.ec/tuportal-internet/accederAplicacion.jspa?redireccion=57&idGrupo=55",
                "https://srienlinea.sri.gob.ec/tuportal-internet/menusFavoritos.jspa?redireccion=57&idGrupo=55",
                "[JSF]");

            if (!string.IsNullOrEmpty(body))
            {
                await _portalSessionService.SubmitJSecurityCheckAsync(s, body);
            }
            else
            {
                Console.WriteLine("[JSF] Sin body de formulario, intentando carga directa...");
            }

            Console.WriteLine("[JSF] Cargando JSF (intento 1)...");
            if (await CargarJsfAsync(s)) return true;

            Console.WriteLine("[JSF] Cargando JSF (intento 2)...");
            return await CargarJsfAsync(s);
        }

        private async Task<bool> CargarJsfAsync(PlaywrightSession s)
        {
            Console.WriteLine("[JSF] Navegando a JSF URL...");
            await s.Page.GotoAsync(JSF_URL, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 25_000 });
            Console.WriteLine($"[JSF] URL actual: {s.Page.Url}");
            await s.CerrarModalAsync();

            Console.WriteLine("[JSF] Esperando selectores de filtros...");
            await s.Page.WaitForSelectorAsync(
                "#frmPrincipal\\:ano, #frmPrincipal\\:cmbTipoComprobante",
                new() { Timeout = 45_000, State = WaitForSelectorState.Visible });

            string vs = await s.Page.EvaluateAsync<string>(
                @"() => document.querySelector('[name=""javax.faces.ViewState""]')?.value ?? ''");

            bool ok = vs.Length >= 20;
            Console.WriteLine(ok
                ? $"[JSF] ✓ ViewState OK ({vs.Length} chars)."
                : $"[JSF] ✗ ViewState inválido ({vs.Length} chars).");
            return ok;
        }

        // ── Paso 3: Consultar tabla ───────────────────────────────────────────────

        private async Task<int> ConsultarAsync(PlaywrightSession s, ReceivedDocumentsQuery dto)
        {
            Console.WriteLine($"[Consulta] Filtros: año={dto.Anio} mes={dto.Mes} día={dto.Dia} tipo={dto.Comprobante}");
            await s.Page.SelectOptionAsync("#frmPrincipal\\:ano", dto.Anio);
            await s.Page.SelectOptionAsync("#frmPrincipal\\:mes", dto.Mes.ToString());
            await s.Page.SelectOptionAsync("#frmPrincipal\\:dia", dto.Dia.ToString());
            await s.Page.SelectOptionAsync("#frmPrincipal\\:cmbTipoComprobante", dto.Comprobante.ToString());
            Console.WriteLine("[Consulta] ✓ Filtros aplicados.");

            Console.WriteLine("[Consulta] Buscando botón consultar...");
            string? btnId = await s.Page.EvaluateAsync<string?>(@"() => {
                for (const id of ['frmPrincipal:btnConsultarSinRe','frmPrincipal:btnBuscar',
                                   'frmPrincipal:btnConsultar','frmPrincipal:j_idt36',
                                   'frmPrincipal:j_idt37','frmPrincipal:j_idt38'])
                    if (document.getElementById(id)) return id;
                for (const b of document.querySelectorAll(
                        '#frmPrincipal button[type=""submit""],#frmPrincipal input[type=""submit""]')) {
                    const t = (b.textContent || b.value || '').trim().toLowerCase();
                    if (t.includes('consultar') || t.includes('buscar')) return b.id || b.name || null;
                }
                return null;
            }");

            if (string.IsNullOrEmpty(btnId))
            {
                Console.WriteLine("[Consulta] ✗ Botón no encontrado.");
                return -1;
            }
            Console.WriteLine($"[Consulta] ✓ Botón: {btnId}");

            for (int i = 1; i <= SriRetryPolicy.QueryAttempts; i++)
            {
                Console.WriteLine($"[Consulta] Intento {i}/4...");
                await s.CerrarModalAsync();

                await s.Page.EvaluateAsync(@"id => {
                    const b = document.getElementById(id);
                    if (!b) return;
                    b.disabled = false;
                    b.removeAttribute('aria-disabled');
                    b.classList.remove('ui-state-disabled');
                    document.querySelector('.ui-messages-warn')?.remove();
                }", btnId);

                string css = $"#{btnId.Replace(":", "\\:")}";
                var box = await s.Page.Locator(css).BoundingBoxAsync();
                if (box is null)
                {
                    Console.WriteLine("[Consulta] Sin bounding box, usando click JS.");
                    await s.Page.EvaluateAsync("id => document.getElementById(id)?.click()", btnId);
                }
                else
                {
                    Console.WriteLine($"[Consulta] Clic en ({box.X + box.Width / 2:F0}, {box.Y + box.Height / 2:F0}).");
                    await s.Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                }

                Console.WriteLine("[Consulta] Esperando resultado (60s)...");
                try
                {
                    await s.Page.WaitForFunctionAsync(@"() => {
                        if (document.querySelector('#frmPrincipal\\:tablaCompRecibidos_paginator_bottom')) return true;
                        if (document.querySelectorAll('#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]').length > 0) return true;
                        const w = document.querySelector('.ui-messages-warn-summary');
                        return w?.textContent.trim().length > 0;
                    }", null, new() { Timeout = 60_000, PollingInterval = 800 });
                }
                catch
                {
                    Console.WriteLine($"[Consulta] ✗ Timeout intento {i}.");
                    continue;
                }

                var r = await s.Page.EvaluateAsync<JsonElement>(@"() => ({
                    filas: document.querySelectorAll('#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]').length,
                    warn:  document.querySelector('.ui-messages-warn-summary')?.textContent?.trim() ?? ''
                })");

                string warn = r.GetProperty("warn").GetString() ?? "";
                int filas = r.GetProperty("filas").GetInt32();
                Console.WriteLine($"[Consulta] filas={filas} | warn='{warn}'");

                if (warn.Contains("No existen datos", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[Consulta] Sin datos para estos filtros.");
                    return 0;
                }
                if (warn.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Consulta] ⚠ Captcha, esperando {SriRetryPolicy.CaptchaBackoff(i).TotalSeconds:0}s...");
                    await Task.Delay(SriRetryPolicy.CaptchaBackoff(i));
                    continue;
                }
                if (filas > 0)
                {
                    Console.WriteLine($"[Consulta] ✓ {filas} filas.");
                    return filas;
                }

                Console.WriteLine("[Consulta] Sin filas ni warning reconocido, reintentando...");
            }

            Console.WriteLine("[Consulta] ✗ Se agotaron los 4 intentos.");
            return -1;
        }

        // ── Paso 4: Descargar ─────────────────────────────────────────────────────

        private async Task DescargarAsync(
            PlaywrightSession s, ReceivedDocumentsQuery dto,
            string dir, int total, SriUserResponse usuario)
        {
            Console.WriteLine($"[Descarga] Esperando tabla ({total} filas)...");
            await s.Page.WaitForSelectorAsync(
                "#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]",
                new() { Timeout = 10_000 });

            var documentType = dto.DocumentType;

            for (int i = 0; i < total; i++)
            {
                Console.WriteLine($"[Descarga] Comprobante {i + 1}/{total}...");
                var comp = await LeerFilaAsync(s, i, total);
                if (comp is null)
                {
                    Console.WriteLine($"[Descarga] ✗ Fila {i} nula, saltando.");
                    continue;
                }

                string ruta = PathService.CombineSafe(
                    dir,
                    comp.NumeroAutorizacion,
                    dto.DescargarXml ? ".xml" : ".pdf",
                    "sin_autorizacion");
                Console.WriteLine($"[Descarga] Ruta: {ruta}");

                bool ok = dto.DescargarXml
                    ? await XmlAsync(s, comp, ruta, documentType)
                    : await PdfAsync(s, comp, ruta);

                if (ok)
                {
                    usuario.Comprobantes.Add(comp);
                    Console.WriteLine($"[Descarga] ✓ OK: {Path.GetFileName(ruta)}");
                }
                else
                {
                    Console.WriteLine($"[Descarga] ✗ Falló: {comp.NumeroAutorizacion}");
                }
            }
        }

        private async Task<ReceivedDocumentResponse?> LeerFilaAsync(PlaywrightSession s, int idx, int total)
        {
            try
            {
                var rows = await s.Page.QuerySelectorAllAsync(
                    "#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]");
                if (idx >= rows.Count)
                {
                    Console.WriteLine($"[Fila] ✗ idx={idx} fuera de rango (DOM={rows.Count}).");
                    return null;
                }

                var rowHtml = await rows[idx].InnerHTMLAsync();
                var comp = ReceivedDocumentsTableParser.ParseRow(rowHtml);
                if (comp is null)
                {
                    Console.WriteLine($"[Fila] ✗ No se pudo parsear fila {idx}.");
                    return null;
                }

                Console.WriteLine($"[Fila] [{idx + 1}/{total}] {comp.NumeroAutorizacion} | {comp.RazonSocial} | Total: {comp.Total}");
                await rows[idx].EvaluateAsync("el => el.scrollIntoView({ block:'center' })");

                return comp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fila] ✗ Error fila {idx}: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> XmlAsync(
            PlaywrightSession s, ReceivedDocumentResponse comp, string ruta, DocumentType documentType)
        {
            if (string.IsNullOrEmpty(comp.XmlLinkId))
            {
                Console.WriteLine("[XML] ✗ XmlLinkId vacío.");
                return false;
            }

            Console.WriteLine($"[XML] Descargando LinkId={comp.XmlLinkId}");
            for (int t = 1; t <= SriRetryPolicy.DownloadAttempts; t++)
            {
                try
                {
                    Console.WriteLine($"[XML] Intento {t}/{SriRetryPolicy.DownloadAttempts}...");
                    string raw = await s.Page.EvaluateAsync<string>(@"async ([lid, url]) => {
                        const vs = document.querySelector(""[name='javax.faces.ViewState']"")?.value;
                        if (!vs) return '';
                        const r = await fetch(url, {
                            method: 'POST', credentials: 'include',
                            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                            body: new URLSearchParams({ 'frmPrincipal':'frmPrincipal',
                                                        'javax.faces.ViewState': vs, [lid]: lid }).toString()
                        });
                        return r.ok ? r.text() : '';
                    }", new[] { comp.XmlLinkId, JSF_URL });

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        Console.WriteLine($"[XML] ✗ Respuesta vacía intento {t}.");
                        await Task.Delay(SriRetryPolicy.DownloadBackoff(t));
                        continue;
                    }

                    string xml = XmlHelper.ExtraerXmlComprobante(raw);
                    if (string.IsNullOrWhiteSpace(xml))
                    {
                        if (raw.TrimStart().StartsWith("<"))
                        {
                            Console.WriteLine("[XML] Usando raw como XML.");
                            xml = raw;
                        }
                        else
                        {
                            Console.WriteLine($"[XML] ✗ No es XML válido (len={raw.Length}).");
                            await Task.Delay(SriRetryPolicy.DownloadBackoff(t));
                            continue;
                        }
                    }

                    await File.WriteAllTextAsync(ruta, xml, Encoding.UTF8);
                    XmlHelper.DeserializarEnComprobante(comp, xml, documentType);
                    comp.RutaArchivo = ruta;
                    Console.WriteLine($"[XML] ✓ Guardado: {Path.GetFileName(ruta)}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[XML] ✗ Excepción intento {t}: {ex.Message}");
                }
            }
            Console.WriteLine($"[XML] ✗ Falló {SriRetryPolicy.DownloadAttempts} intentos.");
            return false;
        }

        private async Task<bool> PdfAsync(PlaywrightSession s, ReceivedDocumentResponse comp, string ruta)
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