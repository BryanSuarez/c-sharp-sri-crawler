using System.Globalization;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.Parsing;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class ReceivedDocumentsPage(ILogger<ReceivedDocumentsPage> logger) : IReceivedDocumentsPage
{
    public const string Url = "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf";
    public async Task<bool> OpenAsync(IReceivedDocumentsSession session)
    {
        logger.LogInformation("Opening the received documents portal.");
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

    public async Task<OperationResult<int>> QueryAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query)
    {
        await session.Page.SelectOptionAsync("#frmPrincipal\\:ano", query.Year.ToString(CultureInfo.InvariantCulture));
        await session.Page.SelectOptionAsync("#frmPrincipal\\:mes", query.Month.ToString());
        await session.Page.SelectOptionAsync("#frmPrincipal\\:dia", query.Day.ToString());
        await session.Page.SelectOptionAsync("#frmPrincipal\\:cmbTipoComprobante", ((int)query.DocumentType).ToString());

        string? btnId = await session.Page.EvaluateAsync<string?>(@"() => {
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
            return OperationResult<int>.Failure(ExtractionErrorCode.QueryFailed, "The portal query could not be completed.");
        }

        for (int i = 1; i <= SriRetryPolicy.QueryAttempts; i++)
        {
            logger.LogInformation("Query attempt {Attempt}/{MaxAttempts}.", i, SriRetryPolicy.QueryAttempts);
            await session.CloseModalAsync();

            await session.Page.EvaluateAsync(@"id => {
                const b = document.getElementById(id);
                if (!b) return;
                b.disabled = false;
                b.removeAttribute('aria-disabled');
                b.classList.remove('ui-state-disabled');
                document.querySelector('.ui-messages-warn')?.remove();
            }", btnId);

            string css = $"#{btnId.Replace(":", "\\:")}";
            var box = await session.Page.Locator(css).BoundingBoxAsync();
            if (box is null)
            {
                await session.Page.EvaluateAsync("id => document.getElementById(id)?.click()", btnId);
            }
            else
            {
                await session.Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
            }

            try
            {
                await session.Page.WaitForFunctionAsync(@"() => {
                    if (document.querySelector('#frmPrincipal\\:tablaCompRecibidos_paginator_bottom')) return true;
                    if (document.querySelectorAll('#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]').length > 0) return true;
                    const w = document.querySelector('.ui-messages-warn-summary');
                    return w?.textContent.trim().length > 0;
                }", null, new() { Timeout = 60_000, PollingInterval = 800 });
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Waiting for the query result failed on attempt {Attempt}.", i);
                continue;
            }

            var r = await session.Page.EvaluateAsync<JsonElement>(@"() => ({
                rowCount: document.querySelectorAll('#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]').length,
                warn:  document.querySelector('.ui-messages-warn-summary')?.textContent?.trim() ?? ''
            })");

            string warn = r.GetProperty("warn").GetString() ?? "";
            int rowCount = r.GetProperty("rowCount").GetInt32();
            logger.LogInformation("Query found {RowCount} visible rows.", rowCount);

            if (warn.Contains("No existen datos", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<int>.Success(0);
            }
            if (warn.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(SriRetryPolicy.CaptchaBackoff(i));
                continue;
            }
            if (rowCount > 0)
            {
                return OperationResult<int>.Success(rowCount);
            }
        }

        return OperationResult<int>.Failure(ExtractionErrorCode.QueryFailed, "The portal query could not be completed.");
    }

    public async Task<OperationResult<ReceivedDocumentReference>> ReadRowAsync(IReceivedDocumentsSession session, int rowIndex)
    {
        try
        {
            await session.Page.WaitForSelectorAsync("#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]", new() { Timeout = 10_000 });
            var rows = await session.Page.QuerySelectorAllAsync(
                "#frmPrincipal\\:tablaCompRecibidos_data tr[data-ri]");
            if (rowIndex >= rows.Count)
            {
                return OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "The document row could not be read.");
            }

            var rowHtml = await rows[rowIndex].InnerHTMLAsync();
            var document = ReceivedDocumentsTableParser.ParseRow(rowHtml);
            if (document is null)
            {
                return OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "The document row could not be read.");
            }

            await rows[rowIndex].EvaluateAsync("el => el.scrollIntoView({ block:'center' })");

            return OperationResult<ReceivedDocumentReference>.Success(document);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Reading received document row {RowIndex} failed.", rowIndex);
            return OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "The document row could not be read.");
        }
    }
}
