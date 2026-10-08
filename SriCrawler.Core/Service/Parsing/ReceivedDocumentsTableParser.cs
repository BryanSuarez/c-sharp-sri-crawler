using DescagaCompronanteSRI.Validation;
using Microsoft.Extensions.Options;
using DescagaCompronanteSRI.Models.Extraction;
using HtmlAgilityPack;
namespace DescagaCompronanteSRI.Services.Parsing;

public static class ReceivedDocumentsTableParser
{
    public static ReceivedDocumentReference? ParseRow(string rowHtml, ReceivedDocumentMetadataParser? parser = null)
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
            // The portal places authorization timestamp before issue date.
            IssuedAt = Text(cells[5]),
            AuthorizedAt = Text(cells[4]),
            RelatedDocuments = cells.Count > 11 ? Attribute(cells[11].SelectSingleNode(".//a[@href]"), "href") : ""
        };
        parser ??= new(Options.Create(new DocumentValidationOptions()));
        metadata = parser.Parse(metadata, new(Text(cells[6]), Text(cells[7]), Text(cells[8]), Text(cells[5]), Text(cells[4])));
        return new(metadata,
            Attribute(cells[9].SelectSingleNode(".//a[@id]"), "id"),
            Attribute(cells[10].SelectSingleNode(".//a[@id]"), "id"),
            Attribute(cells[3].SelectSingleNode(".//a[@id]"), "id"));
    }

    private static string Text(HtmlNode node) => HtmlEntity.DeEntitize(node.InnerText).Trim();
    private static string Attribute(HtmlNode? node, string name) =>
        HtmlEntity.DeEntitize(node?.GetAttributeValue(name, "") ?? "").Trim();

}
