using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Models.Responses;

public sealed class ReceivedDocumentsResponse
{
    public Guid? ExtractionId { get; set; }
    [JsonIgnore] public bool AccumulateDocuments { get; set; } = true;
    [JsonIgnore] public int ProcessedCount { get; set; }
    [JsonIgnore] public int SavedCount { get; set; }
    public required string CompanyId { get; init; }
    public required string TaxpayerId { get; init; }
    public string BusinessName { get; set; } = "";
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Failed;
    public int DiscoveredCount => AccumulateDocuments ? Documents.Count : ProcessedCount;
    public PaginationProgress Pagination { get; } = new();
    public int DownloadedCount => AccumulateDocuments ? Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Downloaded) : SavedCount;
    [JsonIgnore] public int ReusedFiles { get; set; }
    public int ReusedCount => AccumulateDocuments ? Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Downloaded && d.AcquisitionSource == DocumentAcquisitionSource.Reused) : ReusedFiles;
    public int NewlyDownloadedCount => DownloadedCount - ReusedCount;
    [JsonIgnore] public int ValidationIssues { get; set; }
    [JsonIgnore] public int MetadataIssues { get; set; }
    public int ValidationIssueCount => AccumulateDocuments ? Documents.Count(d => d.Validation.Status is DocumentValidationStatus.Invalid or DocumentValidationStatus.Unsupported or DocumentValidationStatus.Failed) : ValidationIssues;
    public int MetadataIssueCount => AccumulateDocuments ? Documents.Count(d => d.MetadataParseStatus is MetadataParseStatus.Partial or MetadataParseStatus.Failed) : MetadataIssues;
    public int FailedCount => DiscoveredCount - DownloadedCount;
    public List<ReceivedDocumentResponse> Documents { get; } = [];
    public List<ExtractionError> Errors { get; } = [];
    [JsonIgnore] public bool QuerySucceeded { get; set; }
}

public sealed class PaginationProgress
{
    public PaginationStatus Status { get; set; } = PaginationStatus.NotStarted;
    public int PagesProcessed { get; set; }
    public int? ReportedTotalCount { get; set; }
    public int DuplicateCount { get; set; }
}
