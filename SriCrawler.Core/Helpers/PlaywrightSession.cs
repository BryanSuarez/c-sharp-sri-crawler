using DescagaCompronanteSRI.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace DescagaCompronanteSRI.Helpers
{
    /// <summary>
    /// Encapsula la creación y limpieza del browser Playwright
    /// y utilidades de página reutilizables.
    /// </summary>
    public sealed class PlaywrightSession : IAsyncDisposable
    {
        public IPage Page { get; private set; } = null!;

        private IPlaywright _pw = null!;
        private IBrowser _browser = null!;
        private IBrowserContext _ctx = null!;

        private ILogger _logger = NullLogger.Instance;

        public static async Task<PlaywrightSession> CreateAsync(ILogger? logger = null)
        {
            var s = new PlaywrightSession { _logger = logger ?? NullLogger.Instance };
            ExtractionDiagnostics.Event(s._logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "browserStarting");
            try { await s.InitAsync(); }
            catch
            {
                await s.DisposeAsync();
                throw;
            }
            ExtractionDiagnostics.Event(s._logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "browserReady");
            return s;
        }

        private async Task InitAsync()
        {
            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "browserLaunching");
            _pw = await Playwright.CreateAsync();

            var headless = !bool.TryParse(Environment.GetEnvironmentVariable("SRI_BROWSER_HEADLESS"), out var configuredHeadless)
                || configuredHeadless;
            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "browserMode", detail: headless);

            _browser = await _pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Channel = "chrome",
                Args = new[]
                {
                    "--headless=new",                                  
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-blink-features=AutomationControlled",    
                    "--disable-dev-shm-usage",
                    "--window-size=1920,1080",
                    "--disable-gpu",
                    "--disable-gpu-sandbox",
                    "--use-gl=swiftshader",
                    "--enable-webgl",
                    "--hide-scrollbars",
                    "--mute-audio",
                    "--use-fake-ui-for-media-stream",
                    "--disable-features=IsolateOrigins,site-per-process",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-infobars",
                }.Where(argument => headless || argument != "--headless=new").ToArray()
            });

            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "contextCreating");
            _ctx = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                Locale = "es-EC",
                TimezoneId = "America/Guayaquil",
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                // User-Agent real de Chrome 136 en Windows — sin esto headless se delata
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                            "AppleWebKit/537.36 (KHTML, like Gecko) " +
                            "Chrome/136.0.0.0 Safari/537.36",
            });

            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "browserInitializing");
            await _ctx.AddInitScriptAsync(@"
                // Ocultar webdriver
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                try { delete navigator.__proto__.webdriver; } catch(e) {}

                // Plugins mínimos para no parecer headless
                Object.defineProperty(navigator, 'plugins',   { get: () => [1,2,3,4,5] });
                Object.defineProperty(navigator, 'languages', { get: () => ['es-EC','es','en-US','en'] });

                // Limpiar variables de automatización
                ['__playwright','__pw_manual','__pw_test',
                 'cdc_adoQpoasnfa76pfcZLmcfl_Array',
                 'cdc_adoQpoasnfa76pfcZLmcfl_Promise',
                 'cdc_adoQpoasnfa76pfcZLmcfl_Symbol'].forEach(k => {
                    try { delete window[k]; } catch(e) {}
                });

                // Falsificar window.chrome (ausente en headless = detección inmediata)
                if (!window.chrome) window.chrome = {};
                if (!window.chrome.runtime) window.chrome.runtime = {
                    connect:     () => ({ onMessage: { addListener: () => {} }, disconnect: () => {} }),
                    sendMessage: () => {},
                    onMessage:   { addListener: () => {}, removeListener: () => {} },
                    id: undefined, getManifest: () => ({}), getURL: s => s, lastError: undefined,
                };
                if (!window.chrome.loadTimes) window.chrome.loadTimes = () => ({});
                if (!window.chrome.csi)       window.chrome.csi = () => ({});
                if (!window.chrome.app)       window.chrome.app = { isInstalled: false };

                // Dimensiones de pantalla (headless devuelve 0 — delata el bot)
                try { Object.defineProperty(screen, 'width',       { get: () => 1920 }); } catch(e) {}
                try { Object.defineProperty(screen, 'height',      { get: () => 1080 }); } catch(e) {}
                try { Object.defineProperty(screen, 'availWidth',  { get: () => 1920 }); } catch(e) {}
                try { Object.defineProperty(screen, 'availHeight', { get: () => 1040 }); } catch(e) {}
                try { Object.defineProperty(window, 'outerWidth',  { get: () => 1920 }); } catch(e) {}
                try { Object.defineProperty(window, 'outerHeight', { get: () => 1080 }); } catch(e) {}

                // Visibilidad — headless aparece como oculto sin esto
                Object.defineProperty(document, 'hidden',          { get: () => false });
                Object.defineProperty(document, 'visibilityState', { get: () => 'visible' });
                document.addEventListener('visibilitychange', e => e.stopImmediatePropagation(), true);

                // WebGL — GPU falso para no devolver cadena vacía
                const _gp = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(p) {
                    if (p === 37445) return 'Intel Inc.';
                    if (p === 37446) return 'Intel Iris OpenGL Engine';
                    return _gp.call(this, p);
                };
            ");

            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "pageCreating");
            Page = await _ctx.NewPageAsync();

            Page.RequestFailed += (_, req) =>
                ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "networkRequestFailed", detail: req.ResourceType);

            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "pageReady");
        }

        /// <summary>Cierra el modal de Material si aparece, por múltiples estrategias.</summary>
        public async Task CerrarModalAsync()
        {
            try
            {
                await Page.WaitForSelectorAsync("mat-dialog-container",
                    new() { Timeout = 3_500, State = WaitForSelectorState.Visible });

                ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "modalClosing");
                await Page.Keyboard.PressAsync("Escape");
                await Task.Delay(600);

                if (await Page.QuerySelectorAsync("mat-dialog-container") != null)
                {
                    var btn = await Page.QuerySelectorAsync(
                        "mat-dialog-container button.sri-boton-cerrar," +
                        "mat-dialog-container button[aria-label='Cerrar dialogo']," +
                        "mat-dialog-container .fa-close");
                    if (btn != null) { await btn.ClickAsync(); await Task.Delay(600); }
                }

                // Último recurso: eliminar del DOM
                if (await Page.QuerySelectorAsync("mat-dialog-container") != null)
                    await Page.EvaluateAsync(@"() => {
                        document.querySelectorAll(
                            'mat-dialog-container,.cdk-overlay-container,' +
                            '.cdk-overlay-backdrop,.cdk-global-overlay-wrapper'
                        ).forEach(e => e.remove());
                        document.body.style.overflow = '';
                        document.body.classList.remove('cdk-global-scrollblock');
                    }");

                ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "modalClosed");
            }
            catch (TimeoutException) { /* sin modal, ignorar */ }
        }

        /// <summary>Mueve el mouse de forma aleatoria para simular actividad humana.</summary>
        public async Task SimularMovimientoMouseAsync(int movimientos = 4)
        {
            var rnd = new Random();
            for (int i = 0; i < movimientos; i++)
            {
                await Page.Mouse.MoveAsync(
                    rnd.Next(100, 1800), rnd.Next(100, 900),
                    new() { Steps = rnd.Next(8, 28) });
                await Task.Delay(rnd.Next(80, 300));
            }
        }

        public async ValueTask DisposeAsync()
        {
            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "sessionClosing");
            try { await Page.CloseAsync(); } catch { }
            try { await _ctx.CloseAsync(); } catch { }
            try { await _browser.CloseAsync(); } catch { }
            try { _pw.Dispose(); } catch { }
            ExtractionDiagnostics.Event(_logger, LogLevel.Debug, DiagnosticEvent.BrowserEvent, code: "sessionClosed");
        }
    }
}
