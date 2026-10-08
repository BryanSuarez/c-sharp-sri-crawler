using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentParser
{
    OperationResult<string> ExtractXml(string response);
    Task<DocumentParseResult> ParseAsync(DocumentContent content, DocumentType documentType);
}
