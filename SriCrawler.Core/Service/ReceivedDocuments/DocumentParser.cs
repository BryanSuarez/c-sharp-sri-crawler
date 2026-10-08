using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Documents.Invoices;
using DescagaCompronanteSRI.Models.Documents.PurchaseSettlements;
using DescagaCompronanteSRI.Models.Documents.CreditNotes;
using DescagaCompronanteSRI.Models.Documents.DebitNotes;
using DescagaCompronanteSRI.Models.Documents.Withholdings;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class DocumentParser : IDocumentParser
{
    public OperationResult<string> ExtractXml(string response)
    {
        string xml = "";
        try
        {
            var element = XDocument.Parse(response).Descendants("comprobante").FirstOrDefault();
            if (element is not null)
            {
                var cdata = element.DescendantNodes().OfType<XCData>().FirstOrDefault();
                xml = cdata?.Value
                    ?? (element.Value.TrimStart().StartsWith("<") ? element.Value
                        : element.Elements().FirstOrDefault()?.ToString() ?? "");
            }
        }
        catch (System.Xml.XmlException) { /* Preserve the existing raw-content fallback. */ }

        if (string.IsNullOrWhiteSpace(xml) && response.TrimStart().StartsWith("<")) xml = response;
        return string.IsNullOrWhiteSpace(xml)
            ? OperationResult<string>.Failure(ExtractionErrorCode.DownloadFailed, "Response does not contain XML content.")
            : OperationResult<string>.Success(xml);
    }

    public async Task<DocumentParseResult> ParseAsync(DocumentContent content, DocumentType documentType)
    {
        if (content.Format == DownloadFormat.Pdf)
            return new(DocumentParseStatus.NotApplicable);

        var type = documentType switch
        {
            DocumentType.Invoice => typeof(Invoice),
            DocumentType.PurchaseSettlement => typeof(PurchaseSettlement),
            DocumentType.CreditNote => typeof(CreditNote),
            DocumentType.DebitNote => typeof(DebitNote),
            DocumentType.Withholding => typeof(WithholdingDocument),
            _ => null
        };
        if (type is null)
            return new(DocumentParseStatus.Unsupported, Error: new(
                ExtractionErrorCode.UnsupportedDocumentType, "Interpretation of this document type is not supported yet."));

        try
        {
            using var reader = new StreamReader(content.Stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var xml = await reader.ReadToEndAsync();
            using var textReader = new StringReader(xml);
            var document = new XmlSerializer(type).Deserialize(textReader);
            return new(DocumentParseStatus.Parsed, document);
        }
        catch (InvalidOperationException)
        {
            return new(DocumentParseStatus.Failed, Error: new(
                ExtractionErrorCode.ParsingFailed, "Document content could not be interpreted."));
        }
        finally
        {
            content.Stream.Position = 0;
        }
    }
}
