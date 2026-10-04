using System.Globalization;
using DescagaCompronanteSRI.Models.Responses;
using HtmlAgilityPack;

namespace DescagaCompronanteSRI.Services.Parsing;

public static class ReceivedDocumentsTableParser
{
    public static ReceivedDocumentResponse? ParseRow(string rowHtml)
    {
        var cells = GetCells(rowHtml);
        if (cells.Count < 11)
        {
            return null;
        }

        var document = new ReceivedDocumentResponse
        {
            RazonSocial = Text(cells[1]),
            TipoDocumento = Text(cells[2]),
            NumeroAutorizacion = Text(cells[3]),
            FechaEmision = Text(cells[4]),
            FechaAutorizacion = Text(cells[5]),
            ImporteTotal = DecimalValue(Text(cells[6])),
            Impuestos = DecimalValue(Text(cells[7])),
            Total = DecimalValue(Text(cells[8])),
            XmlLinkId = Attribute(cells[9].SelectSingleNode(".//a[@id]"), "id"),
            PdfLinkId = Attribute(cells[10].SelectSingleNode(".//a[@id]"), "id"),
            IdDetalle = Attribute(cells[3].SelectSingleNode(".//a[@id]"), "id")
        };

        if (cells.Count > 11)
        {
            document.DocumentosRelacionados = Attribute(cells[11].SelectSingleNode(".//a[@href]"), "href");
        }

        return document;
    }

    private static IReadOnlyList<HtmlNode> GetCells(string rowHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(rowHtml);

        var cells = doc.DocumentNode.SelectNodes("//td[@role='gridcell']")
            ?? doc.DocumentNode.SelectNodes("//td");

        return cells?.ToList() ?? [];
    }

    private static string Text(HtmlNode node) => HtmlEntity.DeEntitize(node.InnerText).Trim();

    private static string Attribute(HtmlNode? node, string name) =>
        HtmlEntity.DeEntitize(node?.GetAttributeValue(name, "") ?? "").Trim();

    private static decimal DecimalValue(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            return currentCultureValue;
        }

        var normalized = value.Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue)
            ? invariantValue
            : 0;
    }
}
