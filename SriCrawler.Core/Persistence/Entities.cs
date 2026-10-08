using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Persistence;

public sealed class ExtractionRecord
{
    public Guid Id { get; set; }
    public string CompanyId { get; set; } = "";
    public string TaxpayerId { get; set; } = "";
    public Guid? ClientRequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string QueryJson { get; set; } = "{}";
    public string? EncryptedPassword { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public JobStatus JobStatus { get; set; }
    public ExtractionStage Stage { get; set; }
    public ExtractionStatus? ExtractionStatus { get; set; }
    public bool QuerySucceeded { get; set; }
    public string BusinessName { get; set; } = "";
    public int AttemptCount { get; set; }
    public Guid? ActiveAttemptId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string PaginationJson { get; set; } = "{}";
}
public sealed class ExtractionAttempt
{
    public Guid Id { get; set; }
    public Guid ExtractionId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorCode { get; set; }
}
public sealed class DocumentRecord
{
    public long Id { get; set; }
    public string CompanyId { get; set; } = "";
    public string TaxpayerId { get; set; } = "";
    public DocumentDirection Direction { get; set; }
    public DocumentType DocumentType { get; set; }
    public string AccessKey { get; set; } = "";
    public string? IssuerTaxpayerId { get; set; }
}
public sealed class DocumentFile
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public DownloadFormat Format { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string StorageJson { get; set; } = "{}";
    public string? LocalPath { get; set; }
    public string ValidationJson { get; set; } = "{}";
    public string? Sha256 { get; set; }
    public long? SizeBytes { get; set; }
    public DocumentValidationStatus ValidationStatus { get; set; }
}
public sealed class DocumentConversion
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public string XmlHash { get; set; } = "";
    public DocumentParseStatus Status { get; set; }
    public string? DocumentJson { get; set; }
    public string? ParserName { get; set; }
    public string? ParserVersion { get; set; }
    public int SchemaVersion { get; set; } = JsonConversion.SchemaVersion;
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ExtractionDocument
{
    public long Id { get; set; }
    public Guid ExtractionId { get; set; }
    public Guid SeenAttemptId { get; set; }
    public string Identity { get; set; } = "";
    public long? DocumentId { get; set; }
    public long? FileId { get; set; }
    public long? ConversionId { get; set; }
    public DocumentDownloadStatus DownloadStatus { get; set; }
    public DocumentParseStatus JsonStatus { get; set; }
    public DocumentValidationStatus ValidationStatus { get; set; }
    public DocumentStorageStatus StorageStatus { get; set; }
    public MetadataParseStatus MetadataParseStatus { get; set; }
    public string ValidationJson { get; set; } = "{}";
    public string? FileHash { get; set; }
    public string? SourceValuesJson { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Taxes { get; set; }
    public decimal? Total { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string ResponseJson { get; set; } = "{}";
}
public sealed class ExtractionFailure
{
    public long Id { get; set; }
    public Guid ExtractionId { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ErrorJson { get; set; } = "{}";
}
public sealed class JobDispatch
{
    public Guid Id { get; set; }
    public Guid ExtractionId { get; set; }
    public string? HangfireJobId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}
