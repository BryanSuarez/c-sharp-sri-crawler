using System.Text.Json;
using DescagaCompronanteSRI.Tests.Validation;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public sealed class ReceivedDocumentsPaginationBrowserTests
{
    [BrowserFact]
    public async Task AsyncPages_RefreshReferencesAndDownloadXmlAndPdfFromEveryPage()
    {
        await using var fixture = await PortalFixture.CreateAsync();
        var result = await fixture.QueryAsync();
        for (var number = 1; number <= 3; number++)
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
            var snapshot = result.Value!;
            Assert.Equal(number, snapshot.PageNumber);
            Assert.Equal(3, snapshot.ReportedTotalCount);
            Assert.Equal(number < 3, snapshot.HasNextPage);
            var document = Assert.Single(snapshot.Rows).Result.Value!;
            Assert.Equal(number.ToString().PadLeft(49, '0'), document.Metadata.AuthorizationNumber);
            Assert.Contains($":{number - 1}:lnkXml", document.XmlLinkId);
            var xml = await new XmlDocumentDownloadStrategy(new DocumentParser(), NullLogger<XmlDocumentDownloadStrategy>.Instance)
                .DownloadAsync(fixture, document);
            Assert.True(xml.IsSuccess);
            await using (var content = xml.Value!)
            {
                Assert.Equal(DocumentValidationStatus.Valid, (await DocumentValidatorTests.Validator().ValidateAsync(content, DocumentType.Invoice, document.Metadata.AuthorizationNumber)).Status);
                using var reader = new StreamReader(content.Stream);
                Assert.Contains($"<secuencial>{number:D9}</secuencial>", await reader.ReadToEndAsync());
            }
            var pdf = await new PdfDocumentDownloadStrategy(NullLogger<PdfDocumentDownloadStrategy>.Instance)
                .DownloadAsync(fixture, document);
            Assert.True(pdf.IsSuccess);
            await using (var content = pdf.Value!)
                Assert.Equal(DocumentValidationStatus.Valid, (await DocumentValidatorTests.Validator().ValidateAsync(content, DocumentType.Invoice, document.Metadata.AuthorizationNumber)).Status);
            if (snapshot.HasNextPage == true) result = await fixture.PageObject.MoveNextAsync(fixture, snapshot);
        }
        Assert.Equal(2, fixture.NavigationRequests);
    }

    [BrowserFact]
    public async Task LegacySriWidget_UsesGlobalVariableWithoutRegistryOrActivePageLinks()
    {
        await using var fixture = await PortalFixture.CreateAsync(legacyWidget: true);
        var result = await fixture.QueryAsync();
        for (var number = 1; number <= 3; number++)
        {
            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(number, result.Value!.PageNumber);
            Assert.Equal(3, result.Value.ReportedTotalCount);
            Assert.Equal(number < 3, result.Value.HasNextPage);
            if (number < 3) result = await fixture.PageObject.MoveNextAsync(fixture, result.Value);
        }
        Assert.Equal(2, fixture.NavigationRequests);
    }

    [BrowserFact]
    public async Task LateResponse_DoesNotDispatchAnotherAdvanceOrSkipAPage()
    {
        await using var fixture = await PortalFixture.CreateAsync(navigationDelay: 650, timeout: 450, advanceEarly: true);
        var first = await fixture.QueryAsync();
        var second = await fixture.PageObject.MoveNextAsync(fixture, first.Value!);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.Equal(2, second.Value!.PageNumber);
        Assert.Equal(1, fixture.NavigationRequests);
        Assert.Equal('2', second.Value.Rows[0].Result.Value!.Metadata.AuthorizationNumber[^1]);
    }

    [BrowserFact]
    public async Task PaginatorPositionAlone_DoesNotConfirmAnAdvance()
    {
        await using var fixture = await PortalFixture.CreateAsync(unchanged: true, advanceEarly: true, timeout: 250);
        var first = await fixture.QueryAsync();
        var next = await fixture.PageObject.MoveNextAsync(fixture, first.Value!);
        Assert.False(next.IsSuccess);
        Assert.Equal(ExtractionErrorCode.PaginationNavigationFailed, next.Error!.Code);
        Assert.Equal(1, fixture.NavigationRequests);
    }

    [BrowserFact]
    public async Task CompletedButUnchangedResponse_RetriesAtMostThreeTimes()
    {
        await using var fixture = await PortalFixture.CreateAsync(unchanged: true, timeout: 250);
        var first = await fixture.QueryAsync();
        var next = await fixture.PageObject.MoveNextAsync(fixture, first.Value!);
        Assert.False(next.IsSuccess);
        Assert.Equal(ExtractionErrorCode.PaginationNavigationFailed, next.Error!.Code);
        Assert.Equal(3, fixture.NavigationRequests);
    }

    [BrowserFact]
    public async Task ExistingRowsAndPaginator_DoNotCompleteAQueryWithoutATableUpdate()
    {
        await using var fixture = await PortalFixture.CreateAsync(staleQuery: true, timeout: 250);
        var query = await fixture.QueryAsync();
        Assert.False(query.IsSuccess);
        Assert.Equal(ExtractionErrorCode.QueryFailed, query.Error!.Code);
    }

    [BrowserFact]
    public async Task MissingResultsTable_CannotBeReportedAsAnExecutedQuery()
    {
        await using var fixture = await PortalFixture.CreateAsync(missingTable: true);
        var result = await fixture.QueryAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal(ExtractionErrorCode.QueryFailed, result.Error!.Code);
    }

    [BrowserFact]
    public async Task MissingPaginationEvidence_IsNotTreatedAsTheLastPage()
    {
        await using var fixture = await PortalFixture.CreateAsync(missingPaginator: true);
        var result = await fixture.QueryAsync();
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PageNumber);
        Assert.Null(result.Value.HasNextPage);
        Assert.Single(result.Value.Rows);
    }

    [BrowserFact]
    public async Task FreshZeroTotal_ConfirmsAnEmptyQuery()
    {
        await using var fixture = await PortalFixture.CreateAsync(empty: true);
        var result = await fixture.QueryAsync();
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEmptyConfirmed);
        Assert.False(result.Value.HasNextPage);
        Assert.Empty(result.Value.Rows);
    }

    private sealed class PortalFixture(PlaywrightSession browser) : IReceivedDocumentsSession
    {
        private const string TableId = "frmPrincipal:tablaCompRecibidos";
        public int NavigationRequests { get; private set; }
        public required ReceivedDocumentsPage PageObject { get; init; }
        public IPage Page => browser.Page;

        public static async Task<PortalFixture> CreateAsync(int navigationDelay = 120, int timeout = 1500,
            bool unchanged = false, bool staleQuery = false, bool missingPaginator = false, bool empty = false, bool advanceEarly = false, bool missingTable = false, bool legacyWidget = false)
        {
            var browser = await PlaywrightSession.CreateAsync();
            var fixture = new PortalFixture(browser)
            {
                PageObject = new(NullLogger<ReceivedDocumentsPage>.Instance, Options.Create(new ReceivedDocumentsPaginationOptions
                {
                    TransitionTimeoutMilliseconds = timeout, RetryDelayMilliseconds = 20
                }))
            };
            var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "received-row.html"));
            var rows = Enumerable.Range(1, 3).Select(number => template
                .Replace("1234567890123456789012345678901234567890123456789", number.ToString().PadLeft(49, '0'))
                .Replace(TableId + ":0:", TableId + $":{number - 1}:")
                .Replace("data-ri=\"0\"", $"data-ri=\"{number - 1}\"" )).ToArray();
            var html = """
                <html><body>CROSS_ORIGIN_FRAME<form id="frmPrincipal">
                <input name="javax.faces.ViewState" value="012345678901234567890123456789"/>
                <select id="frmPrincipal:ano"><option value="2026">2026</option></select>
                <select id="frmPrincipal:mes"><option value="6">6</option></select>
                <select id="frmPrincipal:dia"><option value="0">All</option></select>
                <select id="frmPrincipal:cmbTipoComprobante"><option value="1">Invoice</option></select>
                <button id="frmPrincipal:btnConsultarSinRe" type="button">Consultar</button>
                <table id="frmPrincipal:tablaCompRecibidos"><tbody id="frmPrincipal:tablaCompRecibidos_data">INITIAL</tbody></table>
                <div id="frmPrincipal:tablaCompRecibidos_paginator_bottom">
                  <span class="ui-paginator-page ui-state-active">1</span>
                  <button type="button" class="ui-paginator-next">Next</button>
                </div></form><script>
                const id = 'frmPrincipal:tablaCompRecibidos';
                const rows = ROWS;
                let busy = false, current = 0;
                const cfg = { page: 0, rowCount: 3, rows: 1, pageCount: 3 };
                const paginator = { cfg, getCurrentPage: () => cfg.page, setPage: page => load(page) };
                window.PrimeFaces = { ajax: { Queue: { isEmpty: () => !busy } },
                    widgets: { received: { id, cfg: { paginator: cfg }, getPaginator: () => paginator } } };
                async function load(target, query = false) {
                    busy = true;
                    if (ADVANCE_EARLY && !query) {
                        cfg.page = target;
                        const active = document.querySelector('.ui-state-active');
                        if (active) active.textContent = target + 1;
                    }
                    const body = new URLSearchParams({ 'javax.faces.source': query ? 'frmPrincipal:btnConsultarSinRe' : id,
                        target: String(target) });
                    const response = await (await fetch(location.href, { method: 'POST', body })).json();
                    if (!response.unchanged) {
                        current = target; cfg.page = target;
                        cfg.rowCount = response.empty ? 0 : 3; cfg.pageCount = response.empty ? 1 : 3;
                        document.getElementById(id + '_data').innerHTML = response.empty ? '' : rows[target];
                        const root = document.getElementById(id + '_paginator_bottom');
                        if (root) {
                            const active = root.querySelector('.ui-state-active');
                            if (active) active.textContent = target + 1;
                            const report = root.querySelector('.ui-paginator-current');
                            if (report) report.textContent = '(' + (target + 1) + ' of 3)';
                            root.querySelector('.ui-paginator-next').classList.toggle('ui-state-disabled', target === 2 || response.empty);
                        }
                        document.querySelector('[name="javax.faces.ViewState"]').value = '012345678901234567890-page-' + target;
                    }
                    if (MISSING_TABLE && query) document.getElementById(id).remove();
                    busy = false;
                }
                document.getElementById('frmPrincipal:btnConsultarSinRe').onclick = () => load(0, true);
                document.querySelector('.ui-paginator-next').onclick = () => load(current + 1);
                window.mojarra = { jsfcljs: (form, parameters) => {
                    if (!document.getElementById(Object.keys(parameters)[0])) throw new Error('Stale JSF reference');
                    const link = document.createElement('a');
                    link.href = URL.createObjectURL(new Blob([Uint8Array.from(atob(PDF_DATA[current]), c => c.charCodeAt(0))], { type: 'application/pdf' }));
                    link.download = 'document.pdf'; document.body.appendChild(link); link.click();
                }};
                if (LEGACY_WIDGET) {
                    // Reproduce the SRI: no PrimeFaces.widgets registry and no numeric page links.
                    window.wdvTablaRecibidos = { cfg: { id }, paginator };
                    delete PrimeFaces.widgets;
                    document.querySelector('.ui-state-active').remove();
                    const report = document.createElement('span');
                    report.className = 'ui-paginator-current'; report.textContent = '(1 of 3)';
                    document.getElementById(id + '_paginator_bottom').prepend(report);
                }
                if (MISSING) { delete PrimeFaces.widgets.received; document.getElementById(id + '_paginator_bottom').remove(); }
                </script></body></html>
                """.Replace("CROSS_ORIGIN_FRAME", legacyWidget ? "<iframe src='https://sri-fixture-frame.test/frame'></iframe>" : "")
                .Replace("INITIAL", rows[2]).Replace("ROWS", JsonSerializer.Serialize(rows))
                .Replace("PDF_DATA", JsonSerializer.Serialize(Enumerable.Range(1, 3).Select(number => Convert.ToBase64String(DocumentValidatorTests.Pdf(text: "CLAVE DE ACCESO: " + number.ToString().PadLeft(49, '0')))).ToArray()))
                .Replace("MISSING_TABLE", missingTable ? "true" : "false")
                .Replace("MISSING", missingPaginator ? "true" : "false")
                .Replace("ADVANCE_EARLY", advanceEarly ? "true" : "false")
                .Replace("LEGACY_WIDGET", legacyWidget ? "true" : "false");
            await browser.Page.RouteAsync("**/*", async route =>
            {
                if (route.Request.Url.StartsWith("https://sri-fixture-frame.test/", StringComparison.Ordinal))
                {
                    await route.FulfillAsync(new() { ContentType = "text/html", Body = "<html><body>Cross-origin frame</body></html>" });
                    return;
                }
                if (route.Request.Method != "POST")
                {
                    await route.FulfillAsync(new() { ContentType = "text/html", Body = html });
                    return;
                }
                var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(route.Request.PostData ?? "");
                if (form.ContainsKey("javax.faces.source"))
                {
                    var navigation = form["javax.faces.source"] == TableId;
                    if (navigation) fixture.NavigationRequests++;
                    await Task.Delay(navigation ? navigationDelay : 100);
                    await route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(new
                    {
                        unchanged = navigation ? unchanged : staleQuery, empty
                    }) });
                }
                else
                {
                    var key = form.Keys.Single(k => k.EndsWith(":lnkXml", StringComparison.Ordinal));
                    var number = int.Parse(key.Split(':')[2]) + 1;
                    Assert.Contains($"page-{number - 1}", form["javax.faces.ViewState"].ToString());
                    await route.FulfillAsync(new() { ContentType = "text/xml", Body =
                        DocumentValidatorTests.Fixture().Replace(DocumentValidatorTests.AccessKey(DocumentValidatorTests.Fixture()), number.ToString().PadLeft(49, '0')).Replace("<secuencial>000000000</secuencial>", $"<secuencial>{number:D9}</secuencial>") });
                }
            });
            Assert.True(await fixture.PageObject.OpenAsync(fixture));
            if (legacyWidget) await browser.Page.FrameLocator("iframe").Locator("body").WaitForAsync();
            return fixture;
        }

        public Task<OperationResult<ReceivedDocumentsPageSnapshot>> QueryAsync() =>
            PageObject.QueryAsync(this, ReceivedDocumentsServiceTests.Query());
        public Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query) => throw new NotSupportedException();
        public Task<string> GetPortalBodyAsync() => Task.FromResult("");
        public Task SubmitPortalFormAsync(string body) => Task.CompletedTask;
        public Task CloseModalAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => browser.DisposeAsync();
    }
}
