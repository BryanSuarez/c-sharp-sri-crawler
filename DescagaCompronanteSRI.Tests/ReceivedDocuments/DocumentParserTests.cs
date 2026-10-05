using System.Text;
using System.Xml.Serialization;
using DescagaCompronanteSRI.Models.Documents.Invoices;
using DescagaCompronanteSRI.Models.Documents.PurchaseSettlements;
using DescagaCompronanteSRI.Models.Documents.CreditNotes;
using DescagaCompronanteSRI.Models.Documents.DebitNotes;
using DescagaCompronanteSRI.Models.Documents.Withholdings;
using DescagaCompronanteSRI.Models.Documents.RemissionGuides;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.ReceivedDocuments;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public class DocumentParserTests
{
    [Fact]
    public void ExtractXml_ReturnsCDataPayload()
    {
        const string xml = "<factura id=\"comprobante\" version=\"1.0.0\"></factura>";
        var result = new DocumentParser().ExtractXml($"<partial-response><comprobante><![CDATA[{xml}]]></comprobante></partial-response>");
        Assert.True(result.IsSuccess);
        Assert.Equal(xml, result.Value);
    }

    [Fact]
    public void ExtractXml_KeepsExistingRawFallback()
    {
        const string raw = "<html>Portal error</html>";
        Assert.Equal(raw, new DocumentParser().ExtractXml(raw).Value);
        Assert.False(new DocumentParser().ExtractXml("not XML").IsSuccess);
        Assert.False(new DocumentParser().ExtractXml("").IsSuccess);
    }

    [Theory]
    [InlineData(DocumentType.Invoice, "factura", typeof(Invoice))]
    [InlineData(DocumentType.PurchaseSettlement, "liquidacionCompra", typeof(PurchaseSettlement))]
    [InlineData(DocumentType.CreditNote, "notaCredito", typeof(CreditNote))]
    [InlineData(DocumentType.DebitNote, "notaDebito", typeof(DebitNote))]
    [InlineData(DocumentType.Withholding, "comprobanteRetencion", typeof(WithholdingDocument))]
    public async Task Parse_PreservesSupportedXmlMappings(DocumentType documentType, string root, Type expectedType)
    {
        var xml = $"<{root} id=\"comprobante\" version=\"1.0.0\"><infoTributaria><ruc>1790012345001</ruc><razonSocial>Test Company</razonSocial></infoTributaria></{root}>";
        await using var content = Content(xml);
        var result = await new DocumentParser().ParseAsync(content, documentType);
        Assert.Equal(DocumentParseStatus.Parsed, result.Status);
        Assert.Equal(expectedType, result.Document!.GetType());
        Assert.Equal("comprobante", expectedType.GetProperty("Id")!.GetValue(result.Document));
        var information = expectedType.GetProperty("TaxInformation")!.GetValue(result.Document)!;
        Assert.Equal("1790012345001", information.GetType().GetProperty("TaxpayerId")!.GetValue(information));
        Assert.Equal("Test Company", information.GetType().GetProperty("BusinessName")!.GetValue(information));
        Assert.Equal(0, content.Stream.Position);
    }

    [Fact]
    public async Task Parse_InvoiceRetainsNestedFieldsAndAttributes()
    {
        const string xml = """
            <factura id="comprobante" version="1.0.0">
              <infoFactura><fechaEmision>01/06/2026</fechaEmision><importeTotal>115.58</importeTotal>
                <totalConImpuestos><totalImpuesto><baseImponible>100.50</baseImponible><valor>15.08</valor></totalImpuesto></totalConImpuestos>
              </infoFactura>
              <detalles><detalle><codigoPrincipal>ITEM1</codigoPrincipal><cantidad>1</cantidad><impuestos><impuesto><codigo>2</codigo><valor>15.08</valor></impuesto></impuestos></detalle></detalles>
              <infoAdicional><campoAdicional nombre="email">test@example.test</campoAdicional></infoAdicional>
            </factura>
            """;
        await using var content = Content(xml);
        var parsed = await new DocumentParser().ParseAsync(content, DocumentType.Invoice);
        var invoice = Assert.IsType<Invoice>(parsed.Document);
        Assert.Equal("115.58", invoice.InvoiceInformation!.TotalAmount);
        Assert.Equal("100.50", invoice.InvoiceInformation.TaxTotals!.TotalTax!.TaxableBase);
        Assert.Equal("ITEM1", Assert.Single(invoice.Items!.Items).PrimaryCode);
        Assert.Equal("15.08", invoice.Items.Items[0].Taxes!.Tax.Single().Value);
        Assert.Equal("email", invoice.AdditionalInformation!.AdditionalField[0].Name);
        Assert.Equal("test@example.test", invoice.AdditionalInformation.AdditionalField[0].Text);
    }

    [Theory]
    [InlineData(DocumentType.RemissionGuide)]
    [InlineData(DocumentType.RemissionGuideAlternative)]
    public async Task Parse_GuideRemainsExplicitlyUnsupported(DocumentType documentType)
    {
        await using var content = Content("<guiaRemision/>");
        var result = await new DocumentParser().ParseAsync(content, documentType);
        Assert.Equal(DocumentParseStatus.Unsupported, result.Status);
        Assert.Null(result.Document);
        Assert.Equal(ExtractionErrorCode.UnsupportedDocumentType, result.Error!.Code);
    }

    [Fact]
    public void GuideModel_PreservesMappingEvenThoughInterpretationIsDeferred()
    {
        const string xml = "<guiaRemision id=\"comprobante\"><infoTributaria><ruc>1790012345001</ruc></infoTributaria><infoGuiaRemision><placa>ABC123</placa></infoGuiaRemision></guiaRemision>";
        using var reader = new StringReader(xml);
        var guide = Assert.IsType<RemissionGuide>(new XmlSerializer(typeof(RemissionGuide)).Deserialize(reader));
        Assert.Equal("1790012345001", guide.TaxInformation!.TaxpayerId);
        Assert.Equal("ABC123", guide.RemissionGuideInformation!.LicensePlate);
    }

    [Fact]
    public async Task Parse_InvalidXmlReportsFailureAndRewindsContent()
    {
        await using var content = Content("<html>error</html>");
        var result = await new DocumentParser().ParseAsync(content, DocumentType.Invoice);
        Assert.Equal(DocumentParseStatus.Failed, result.Status);
        Assert.Equal(ExtractionErrorCode.ParsingFailed, result.Error!.Code);
        Assert.Equal(0, content.Stream.Position);
    }

    internal static DocumentContent Content(string text) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(text)), DownloadFormat.Xml);
}
