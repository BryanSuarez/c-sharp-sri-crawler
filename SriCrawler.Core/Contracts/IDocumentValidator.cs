using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentValidator
{
    string Version { get; }
    Task<DocumentValidationResult> ValidateAsync(DocumentContent content, DocumentType type, string accessKey, CancellationToken token = default);
}
public interface IDocumentFormatValidator : IDocumentValidator
{
    DownloadFormat Format { get; }
}
