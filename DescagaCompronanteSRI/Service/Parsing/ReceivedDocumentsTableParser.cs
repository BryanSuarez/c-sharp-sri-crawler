using System.Globalization;
using DescagaCompronanteSRI.Models.Extraction;
using HtmlAgilityPack;
namespace DescagaCompronanteSRI.Services.Parsing;

public static class ReceivedDocumentsTableParser
{
    public static ReceivedDocumentReference? ParseRow(string rowHtml)
    {
        var document = new HtmlDocument();
        document.LoadHtml(rowHtml);
        var cells = (document.DocumentNode.SelectNodes("//td[@role='gridcell']")
            ?? document.DocumentNode.SelectNodes("//td"))?.ToList();
        if (cells is null || cells.Count < 11) return null;
        var metadata = new ReceivedDocumentMetadata
        {
            SupplierBusinessName = Text(cells[1]),
            DocumentTypeName = Text(cells[2]),
            AuthorizationNumber = Text(cells[3]),
            IssuedAt = Text(cells[4]),
            AuthorizedAt = Text(cells[5]),
            Amount = DecimalValue(Text(cells[6])),
            Taxes = DecimalValue(Text(cells[7])),
            Total = DecimalValue(Text(cells[8])),
            RelatedDocuments = cells.Count > 11 ? Attribute(cells[11].SelectSingleNode(".//a[@href]"), "href") : ""
        };
        return new(metadata,
            Attribute(cells[9].SelectSingleNode(".//a[@id]"), "id"),
            Attribute(cells[10].SelectSingleNode(".//a[@id]"), "id"),
            Attribute(cells[3].SelectSingleNode(".//a[@id]"), "id"));
    }

    private static string Text(HtmlNode node) => HtmlEntity.DeEntitize(node.InnerText).Trim();
    private static string Attribute(HtmlNode? node, string name) =>
        HtmlEntity.DeEntitize(node?.GetAttributeValue(name, "") ?? "").Trim();

    private static decimal DecimalValue(string value)
    {
        // Locale handling is intentionally preserved for this structural refactor.
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentCultureValue))
            return currentCultureValue;
        return decimal.TryParse(value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue)
            ? invariantValue : 0;
    }
}
