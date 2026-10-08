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
