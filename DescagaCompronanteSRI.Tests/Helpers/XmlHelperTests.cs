using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Responses;

namespace DescagaCompronanteSRI.Tests.Helpers;

public class XmlHelperTests
{
    [Fact]
    public void ExtraerXmlComprobante_ReturnsCDataPayload()
    {
        const string expectedXml = "<factura id=\"comprobante\" version=\"1.0.0\"></factura>";
        var response = $"<partial-response><comprobante><![CDATA[{expectedXml}]]></comprobante></partial-response>";

        var xml = XmlHelper.ExtraerXmlComprobante(response);

        Assert.Equal(expectedXml, xml);
    }

    [Fact]
    public void DeserializarEnComprobante_AssignsInvoicePayload()
    {
        const string xml = "<factura id=\"comprobante\" version=\"1.0.0\"><infoTributaria><ruc>1790012345001</ruc></infoTributaria></factura>";
        var document = new ReceivedDocumentResponse();

        XmlHelper.DeserializarEnComprobante(document, xml, DocumentType.Invoice);

        Assert.NotNull(document.Factura);
        Assert.Equal("comprobante", document.Factura.Id);
        Assert.Equal("1790012345001", document.Factura.InfoTributariaFactura.Ruc);
    }
}
