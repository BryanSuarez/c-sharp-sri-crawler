using System.Text;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public class ReceivedDocumentsBrowserTests
{
    [BrowserFact]
    public async Task PageAndStrategies_WorkWithLocalPortalFixtureAndIssuedPdfStillDownloads()
    {
        await using var browser = await PlaywrightSession.CreateAsync();
        var row = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "received-row.html"));
        const string xml = "<factura id=\"comprobante\" version=\"1.0.0\"><infoTributaria><ruc>1790012345001</ruc></infoTributaria></factura>";
        var html = """
            <html><body><form id="frmPrincipal">
            <input name="javax.faces.ViewState" value="012345678901234567890123456789"/>
            <select id="frmPrincipal:ano"><option value="2026">2026</option></select>
            <select id="frmPrincipal:mes"><option value="6">6</option></select>
            <select id="frmPrincipal:dia"><option value="0">All</option></select>
            <select id="frmPrincipal:cmbTipoComprobante"><option value="1">Invoice</option></select>
            <button id="frmPrincipal:btnConsultarSinRe" type="button">Consultar</button>
            <table><tbody id="frmPrincipal:tablaCompRecibidos_data">ROW</tbody></table>
            </form><script>
            window.mojarra = { jsfcljs: function() {
                const link = document.createElement('a');
                link.href = URL.createObjectURL(new Blob(['%PDF-1.4\nSynthetic test'], {type:'application/pdf'}));
                link.download = 'sample.pdf';
                document.body.appendChild(link); link.click();
            }};
            </script></body></html>
            """.Replace("ROW", row);
        await browser.Page.RouteAsync("**/*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = route.Request.Method == "POST" ? "text/xml" : "text/html",
            Body = route.Request.Method == "POST"
                ? $"<partial-response><comprobante><![CDATA[{xml}]]></comprobante></partial-response>"
                : html
        }));
        var session = new BrowserSession(browser);
        var page = new ReceivedDocumentsPage(NullLogger<ReceivedDocumentsPage>.Instance);
        Assert.True(await page.OpenAsync(session));
        var queried = await page.QueryAsync(session, ReceivedDocumentsServiceTests.Query());
        Assert.True(queried.IsSuccess);
        Assert.Equal(1, queried.Value);
        var rowResult = await page.ReadRowAsync(session, 0);
        Assert.True(rowResult.IsSuccess);
        var document = rowResult.Value!;
        Assert.Equal("1234567890123456789012345678901234567890123456789", document.Metadata.AuthorizationNumber);

        var parser = new DocumentParser();
        var xmlResult = await new XmlDocumentDownloadStrategy(parser, NullLogger<XmlDocumentDownloadStrategy>.Instance)
            .DownloadAsync(session, document);
        Assert.True(xmlResult.IsSuccess);
        await using var xmlContent = xmlResult.Value!;
        var parsed = await parser.ParseAsync(xmlContent, DocumentType.Invoice);
        Assert.Equal(DocumentParseStatus.Parsed, parsed.Status);

        var pdfResult = await new PdfDocumentDownloadStrategy(NullLogger<PdfDocumentDownloadStrategy>.Instance)
            .DownloadAsync(session, document);
        Assert.True(pdfResult.IsSuccess);
        await using var pdfContent = pdfResult.Value!;
        using var reader = new StreamReader(pdfContent.Stream, Encoding.UTF8, leaveOpen: true);
        Assert.StartsWith("%PDF", await reader.ReadToEndAsync());

        var path = Path.Combine(Path.GetTempPath(), "sri-issued-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            Assert.True(await new PdfDownloadService().DownloadAsync(browser, document.PdfLinkId, path));
            Assert.StartsWith("%PDF", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class BrowserSession(PlaywrightSession session) : IReceivedDocumentsSession
    {
        public IPage Page => session.Page;
        public Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query) => throw new NotSupportedException();
        public Task<string> GetPortalBodyAsync() => Task.FromResult("");
        public Task SubmitPortalFormAsync(string body) => Task.CompletedTask;
        public Task CloseModalAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SRI_BROWSER_TESTS") != "1")
            Skip = "Set SRI_BROWSER_TESTS=1 to run the local Chrome fixture (no SRI access).";
    }
}
