using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Models.Responses;

public sealed class ReceivedDocumentsResponse
{
    public required string TaxpayerId { get; init; }
    public string BusinessName { get; set; } = "";
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Failed;
    public int DiscoveredCount { get; set; }
    public int DownloadedCount => Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Downloaded);
    public int FailedCount => Documents.Count(d => d.DownloadStatus == DocumentDownloadStatus.Failed);
    public List<ReceivedDocumentResponse> Documents { get; } = [];
    public List<ExtractionError> Errors { get; } = [];
    [JsonIgnore] public bool QuerySucceeded { get; set; }
}
