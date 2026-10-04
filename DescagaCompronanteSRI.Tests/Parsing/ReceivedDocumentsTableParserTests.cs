using DescagaCompronanteSRI.Services.Parsing;

namespace DescagaCompronanteSRI.Tests.Parsing;

public class ReceivedDocumentsTableParserTests
{
    [Fact]
    public void ParseRow_ReturnsReceivedDocument()
    {
        var html = Fixture("received-row.html");

        var document = ReceivedDocumentsTableParser.ParseRow(html);

        Assert.NotNull(document);
        Assert.Equal("PROVEEDOR DE PRUEBA S.A.", document.RazonSocial);
        Assert.Equal("Factura", document.TipoDocumento);
        Assert.Equal("1234567890123456789012345678901234567890123456789", document.NumeroAutorizacion);
        Assert.Equal(100.50m, document.ImporteTotal);
        Assert.Equal(15.08m, document.Impuestos);
        Assert.Equal(115.58m, document.Total);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkXml", document.XmlLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkPdf", document.PdfLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:j_idt48", document.IdDetalle);
        Assert.Equal("/docs/relacionados", document.DocumentosRelacionados);
    }

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
