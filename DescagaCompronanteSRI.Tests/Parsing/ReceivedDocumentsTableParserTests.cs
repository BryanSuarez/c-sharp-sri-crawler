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
        Assert.Equal("01/06/2026", document.Metadata.IssuedAt);
        Assert.Equal("01/06/2026 10:30:00", document.Metadata.AuthorizedAt);
        Assert.Equal(new DateOnly(2026, 6, 1), document.Metadata.IssuedDate);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.FromHours(-5)), document.Metadata.AuthorizedAtIso);
        Assert.Equal(DescagaCompronanteSRI.Models.Enums.MetadataParseStatus.Parsed, document.Metadata.MetadataParseStatus);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkXml", document.XmlLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:lnkPdf", document.PdfLinkId);
        Assert.Equal("frmPrincipal:tablaCompRecibidos:0:j_idt48", document.DetailId);
        Assert.Equal("/docs/relacionados", document.Metadata.RelatedDocuments);
    }

    private static string Fixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
