using System.Globalization;
using System.Text.RegularExpressions;
using DescagaCompronanteSRI.Models.Responses;
using HtmlAgilityPack;

namespace DescagaCompronanteSRI.Services.Parsing;

public static class IssuedDocumentsTableParser
{
    public static IssuedDocumentResponse? ParseRow(string rowHtml)
    {
        var cells = GetCells(rowHtml);
        if (cells.Count < 10)
        {
            return null;
        }

        var accessKeyLink = cells[2].SelectSingleNode(".//a[@id]");
        var pdfLink = cells[8].SelectSingleNode(".//a[contains(@id, 'lnkPdf')]")
            ?? cells[8].SelectSingleNode(".//a[@id]");
        var relatedDocumentsLink = cells[9].SelectSingleNode(".//a[@id]");

        var seriesType = Text(cells[1]);
        var accessKey = !string.IsNullOrWhiteSpace(Text(accessKeyLink))
            ? Text(accessKeyLink!)
            : Text(cells[2]);

        return new IssuedDocumentResponse
        {
            TipoSerie = seriesType,
            NumeroFactura = Regex.Match(seriesType, @"\d{3}-\d{3}-\d{9}").Value,
            ClaveAcceso = accessKey,
            IdDetalleLinkId = Attribute(accessKeyLink, "id"),
            FechaHoraAuth = Text(cells[3]),
            FechaEmision = Text(cells[4]),
            ValorSinImpuestos = DecimalValue(Text(cells[5])),
            Iva = DecimalValue(Text(cells[6])),
            ImporteTotal = DecimalValue(Text(cells[7])),
            PdfLinkId = Attribute(pdfLink, "id"),
            DocsRelLinkId = Attribute(relatedDocumentsLink, "id")
        };
    }

    private static IReadOnlyList<HtmlNode> GetCells(string rowHtml)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(rowHtml);

        var cells = doc.DocumentNode.SelectNodes("//td[@role='gridcell']")
            ?? doc.DocumentNode.SelectNodes("//td");

        return cells?.ToList() ?? [];
    }

    private static string Text(HtmlNode? node) => HtmlEntity.DeEntitize(node?.InnerText ?? "").Trim();

    private static string Attribute(HtmlNode? node, string name) =>
        HtmlEntity.DeEntitize(node?.GetAttributeValue(name, "") ?? "").Trim();

    private static decimal DecimalValue(string value)
    {
        var normalized = value.Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue)
            ? invariantValue
            : 0;
    }
}
