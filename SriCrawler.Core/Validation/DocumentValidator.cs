using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Validation;

public sealed class DocumentValidator(IEnumerable<IDocumentFormatValidator> validators) : IDocumentValidator
{
    public const string CurrentVersion = "1";
    public string Version => CurrentVersion;
    private readonly IReadOnlyDictionary<DownloadFormat, IDocumentFormatValidator> formats = validators.ToDictionary(x => x.Format);
    public Task<DocumentValidationResult> ValidateAsync(DocumentContent content, DocumentType type, string accessKey, CancellationToken token = default) =>
        formats.TryGetValue(content.Format, out var validator) ? validator.ValidateAsync(content, type, accessKey, token)
        : Task.FromResult(new DocumentValidationResult { Status = DocumentValidationStatus.Unsupported,
            ValidatorVersion = Version, ValidatedAt = DateTimeOffset.UtcNow,
            Errors = [new(ExtractionErrorCode.ValidationFailed, "Document format is not supported.")] });
}
