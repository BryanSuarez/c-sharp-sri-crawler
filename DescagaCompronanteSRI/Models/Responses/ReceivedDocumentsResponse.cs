using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Models.Responses;

public sealed class ReceivedDocumentsResponse
{
    public required string CompanyId { get; init; }
    public required string TaxpayerId { get; init; }
    public string BusinessName { get; set; } = "";
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Failed;
    public int DiscoveredCount => Documents.Count;
    public PaginationProgress Pagination { get; } = new();
    public int DownloadedCount => Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Downloaded);
    public int FailedCount => Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Failed);
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
