using DescagaCompronanteSRI.Diagnostics;
using System.Globalization;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.Parsing;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class ReceivedDocumentsPage(ILogger<ReceivedDocumentsPage> logger, IOptions<ReceivedDocumentsPaginationOptions> options, ReceivedDocumentMetadataParser? metadataParser = null) : IReceivedDocumentsPage
{
    public const string Url = "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf";
    public async Task<bool> OpenAsync(IReceivedDocumentsSession session)
    {
        ExtractionDiagnostics.Safely(() => logger.LogInformation("Opening the received documents portal."));
        string body = await session.GetPortalBodyAsync();

        if (!string.IsNullOrEmpty(body))
        {
            await session.SubmitPortalFormAsync(body);
        }

        if (await LoadPageAsync(session)) return true;

        return await LoadPageAsync(session);
    }

    private async Task<bool> LoadPageAsync(IReceivedDocumentsSession session)
    {
        await session.Page.GotoAsync(Url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 25_000 });

        await session.CloseModalAsync();

        await session.Page.WaitForSelectorAsync(
            "#frmPrincipal\\:ano, #frmPrincipal\\:cmbTipoComprobante",
            new() { Timeout = 45_000, State = WaitForSelectorState.Visible });

        string vs = await session.Page.EvaluateAsync<string>(
            @"() => document.querySelector('[name=""javax.faces.ViewState""]')?.value ?? ''");

        bool ok = vs.Length >= 20;

        return ok;
    }

    public async Task<OperationResult<ReceivedDocumentsPageSnapshot>> QueryAsync(
        IReceivedDocumentsSession session, ReceivedDocumentsQuery query)
    {
        await session.Page.SelectOptionAsync("[id='frmPrincipal:ano']", query.Year.ToString(CultureInfo.InvariantCulture));
        await session.Page.SelectOptionAsync("[id='frmPrincipal:mes']", query.Month.ToString(CultureInfo.InvariantCulture));
        await session.Page.SelectOptionAsync("[id='frmPrincipal:dia']", query.Day.ToString(CultureInfo.InvariantCulture));
        await session.Page.SelectOptionAsync("[id='frmPrincipal:cmbTipoComprobante']", ((int)query.DocumentType).ToString(CultureInfo.InvariantCulture));
        var buttonId = await session.Page.EvaluateAsync<string?>("""
            () => {
                for (const id of ['frmPrincipal:btnConsultarSinRe','frmPrincipal:btnBuscar',
                    'frmPrincipal:btnConsultar','frmPrincipal:j_idt36','frmPrincipal:j_idt37','frmPrincipal:j_idt38'])
                    if (document.getElementById(id)) return id;
                for (const b of document.querySelectorAll('#frmPrincipal button[type="submit"],#frmPrincipal input[type="submit"]'))
                    if (/consultar|buscar/i.test(b.textContent || b.value || '')) return b.id || b.name || null;
                return null;
            }
            """);
        if (buttonId is null) return Failure(ExtractionErrorCode.QueryFailed, "The query control could not be found.");
        await session.CloseModalAsync();
        await session.Page.EvaluateAsync("() => document.querySelector('.ui-messages-warn')?.remove()");
        return await ExecuteAsync(session, buttonId, null, SriRetryPolicy.QueryAttempts, async () =>
        {
            await session.Page.EvaluateAsync("""
                id => {
                    const button = document.getElementById(id);
                    button.disabled = false;
                    button.removeAttribute('aria-disabled');
                    button.classList.remove('ui-state-disabled');
                }
                """, buttonId);
            await session.Page.Locator($"[id='{buttonId}']").ClickAsync(new() { Timeout = options.Value.TransitionTimeoutMilliseconds });
            return true;
        });
    }

    public Task<OperationResult<ReceivedDocumentsPageSnapshot>> MoveNextAsync(
        IReceivedDocumentsSession session, ReceivedDocumentsPageSnapshot current) =>
        ExecuteAsync(session, ReceivedDocumentsDom.TableId, current.PageNumber,
            options.Value.NavigationAttempts, () => session.Page.EvaluateAsync<bool>(ReceivedDocumentsDom.ClickNext));

    private async Task<OperationResult<ReceivedDocumentsPageSnapshot>> ExecuteAsync(
        IReceivedDocumentsSession session, string source, int? previousPage, int attempts, Func<Task<bool>> click)
    {
        var page = session.Page;
        var code = previousPage is null ? ExtractionErrorCode.QueryFailed : ExtractionErrorCode.PaginationNavigationFailed;
        using var action = new ReceivedDocumentsPortalAction(page, source);
        var clicked = false;
        var captcha = false;
        try
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                if (attempt > 1)
                {
                    ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: captcha ? "captchaRetry" : "portalTransitionRetry", detail: attempt);
                    await Task.Delay(captcha ? SriRetryPolicy.CaptchaBackoff(attempt - 1)
                        : TimeSpan.FromMilliseconds(options.Value.RetryDelayMilliseconds * (attempt - 1)));
                }
                var timer = Stopwatch.StartNew();
                await action.RefreshAsync();
                var state = await page.EvaluateAsync<JsonElement>(ReceivedDocumentsDom.ReadState);
                if (clicked && IsUpdated(state, action))
                {
                    if (!IsCaptcha(state) && (previousPage is null || PageNumber(state) != previousPage))
                        return await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.TableRead, () => SnapshotAsync(page, code),
                            value => value.IsSuccess ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed);
                }
                // A timed-out response may still arrive. Never dispatch another advance while it is pending,
                // or while the portal already moved its paginator ahead of its table update.
                if (!clicked || !action.HasPendingRequest && !state.GetProperty("busy").GetBoolean() &&
                    (previousPage is null || PageNumber(state) == previousPage) &&
                    (action.ResponseCompleted || action.ResponseFailed))
                {
                    action.Reset();
                    await page.EvaluateAsync(ReceivedDocumentsDom.InstallObserver);
                    if (!await click()) return Failure(code, "The next-page control could not be activated.");
                    clicked = true;
                }
                while (timer.ElapsedMilliseconds < options.Value.TransitionTimeoutMilliseconds)
                {
                    await action.RefreshAsync();
                    state = await page.EvaluateAsync<JsonElement>(ReceivedDocumentsDom.ReadState);
                    if (IsUpdated(state, action))
                    {
                        if (IsCaptcha(state)) { captcha = true; break; }
                        if (previousPage is null || PageNumber(state) != previousPage)
                            return await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.TableRead, () => SnapshotAsync(page, code),
                            value => value.IsSuccess ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed);
                    }
                    await Task.Delay(50);
                }
                ExtractionDiagnostics.Safely(() => logger.LogWarning("Portal transition to page {Page} was not confirmed on attempt {Attempt}/{Attempts}.",
                    previousPage is null ? 1 : previousPage + 1, attempt, attempts));
                ExtractionDiagnostics.Safely(() => logger.LogWarning("Portal action evidence: response completed {Completed}, response failed {Failed}, pending {Pending}, CAPTCHA rejected {Captcha}, table revision {Revision}, message revision {MessageRevision}.",
                    action.ResponseCompleted, action.ResponseFailed, action.HasPendingRequest, IsCaptcha(state),
                    state.GetProperty("revision").GetInt32(), state.GetProperty("messageRevision").GetInt32()));
            }
            return Failure(code, captcha ? "SRI CAPTCHA validation failed after the query retries." : "The portal response and expected table transition could not be confirmed.");
        }
        catch (Exception exception)
        {
            ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.TransportRetry, code: "portalActionFailed", errorType: exception.GetType().Name);
            return Failure(code, "The portal table transition could not be completed.");
        }
        finally
        {
            try { await page.EvaluateAsync("() => window.__receivedProbe?.observer.disconnect()"); }
            catch (PlaywrightException) { /* The session may have closed during navigation. */ }
        }
    }

    private static bool IsUpdated(JsonElement state, ReceivedDocumentsPortalAction action) =>
        action.ResponseCompleted && !action.HasPendingRequest && !state.GetProperty("busy").GetBoolean() &&
        state.GetProperty("settled").GetBoolean() &&
        (state.GetProperty("revision").GetInt32() > 0 || state.GetProperty("freshDocument").GetBoolean() ||
         state.GetProperty("messageRevision").GetInt32() > 0 && (IsCaptcha(state) || state.GetProperty("empty").GetBoolean()));

    private static bool IsCaptcha(JsonElement state) =>
        (state.GetProperty("warn").GetString() ?? "").Contains("captcha", StringComparison.OrdinalIgnoreCase);
    private static int? PageNumber(JsonElement state) => NullableInt(state, "page");
    private static int? NullableInt(JsonElement state, string name) =>
        state.GetProperty(name).ValueKind == JsonValueKind.Number ? state.GetProperty(name).GetInt32() : null;
    private static OperationResult<ReceivedDocumentsPageSnapshot> Failure(ExtractionErrorCode code, string message) =>
        OperationResult<ReceivedDocumentsPageSnapshot>.Failure(code, message);

    private async Task<OperationResult<ReceivedDocumentsPageSnapshot>> SnapshotAsync(IPage page, ExtractionErrorCode failureCode)
    {
        // Capture metadata and references in the same browser turn to avoid mixing two page states.
        var capture = await page.EvaluateAsync<JsonElement>(
            $"() => ({{ state: ({ReceivedDocumentsDom.ReadState})(), rows: ({ReceivedDocumentsDom.ReadRows})() }})");
        var state = capture.GetProperty("state");
        if (!state.GetProperty("tablePresent").GetBoolean() && !state.GetProperty("empty").GetBoolean())
            return Failure(failureCode, "The received results table was not returned by the portal.");
        var data = capture.GetProperty("rows");
        var rows = new List<ReceivedDocumentRowSnapshot>();
        foreach (var row in data.EnumerateArray())
        {
            ReceivedDocumentReference? document;
            try { document = ReceivedDocumentsTableParser.ParseRow(row.GetProperty("html").GetString()!, metadataParser); }
            catch (Exception) { document = null; }
            rows.Add(new(rows.Count, document is null
                ? OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "The document row could not be read.")
                : OperationResult<ReceivedDocumentReference>.Success(document),
                document?.Metadata.AuthorizationNumber is { Length: 49 } key && key.All(char.IsAsciiDigit)
                    ? "key:" + key : "row:" + row.GetProperty("identity").GetString()));
        }
        var next = state.GetProperty("hasNext");
        return OperationResult<ReceivedDocumentsPageSnapshot>.Success(new(PageNumber(state),
            next.ValueKind == JsonValueKind.Null ? null : next.GetBoolean(), NullableInt(state, "total"), rows,
            state.GetProperty("empty").GetBoolean(), state.GetProperty("inconsistent").GetBoolean()
                ? new(ExtractionErrorCode.PaginationInconsistent, "Portal pagination signals contradict one another.") : null));
    }
}
