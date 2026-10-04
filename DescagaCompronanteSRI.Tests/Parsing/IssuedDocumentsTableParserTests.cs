using DescagaCompronanteSRI.Services.Parsing;

namespace DescagaCompronanteSRI.Tests.Parsing;

public class IssuedDocumentsTableParserTests
{
    [Fact]
    public void ParseRow_ReturnsIssuedDocument()
    {
        var html = Fixture("issued-row.html");

        var document = IssuedDocumentsTableParser.ParseRow(html);

        Assert.NotNull(document);
        Assert.Equal("Factura 001-001-000000011", document.TipoSerie);
        Assert.Equal("001-001-000000011", document.NumeroFactura);
        Assert.Equal("1234567890123456789012345678901234567890123456789", document.ClaveAcceso);
        Assert.Equal("frmPrincipal:tablaCompEmitidos:0:j_idt53", document.IdDetalleLinkId);
        Assert.Equal("15/01/2026 21:38:04", document.FechaHoraAuth);
        Assert.Equal("15/01/2026", document.FechaEmision);
        Assert.Equal(661.56m, document.ValorSinImpuestos);
        Assert.Equal(99.23m, document.Iva);
        Assert.Equal(760.79m, document.ImporteTotal);
        Assert.Equal("frmPrincipal:tablaCompEmitidos:0:lnkPdf", document.PdfLinkId);
        Assert.Equal("frmPrincipal:tablaCompEmitidos:0:j_idt78", document.DocsRelLinkId);
    }

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
