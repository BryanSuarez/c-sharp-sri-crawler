using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Extraction;

public sealed record DocumentValidationResult
{
    public DocumentValidationStatus Status { get; init; }
    public DocumentIdentityStatus IdentityStatus { get; init; }
    public string? ValidatorVersion { get; init; }
    public string? Schema { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public DateTimeOffset? ValidatedAt { get; init; }
    public IReadOnlyList<string> Checks { get; init; } = [];
    public IReadOnlyList<ExtractionError> Errors { get; init; } = [];
}
