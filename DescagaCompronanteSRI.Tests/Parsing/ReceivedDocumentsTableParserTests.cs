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
        Assert.Equal("PROVEEDOR DE PRUEBA S.A.", document.Metadata.SupplierBusinessName);
        Assert.Equal("Factura", document.Metadata.DocumentTypeName);
        Assert.Equal("1234567890123456789012345678901234567890123456789", document.Metadata.AuthorizationNumber);
        Assert.Equal(100.50m, document.Metadata.Amount);
        Assert.Equal(15.08m, document.Metadata.Taxes);
        Assert.Equal(115.58m, document.Metadata.Total);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkXml", document.XmlLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkPdf", document.PdfLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:j_idt48", document.DetailId);
        Assert.Equal("/docs/relacionados", document.Metadata.RelatedDocuments);
    }

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
