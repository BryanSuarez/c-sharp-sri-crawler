using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
namespace DescagaCompronanteSRI.Models.Responses;

public sealed class ReceivedDocumentResponse
{
    public int PageNumber { get; init; } = 1;
    public int RowIndex { get; init; }
    public ReceivedDocumentMetadata? Metadata { get; set; }
    public DownloadFormat DownloadFormat { get; init; }
    public DocumentDownloadStatus DownloadStatus { get; set; } = DocumentDownloadStatus.Failed;
    public DocumentParseStatus ParseStatus { get; set; } = DocumentParseStatus.NotApplicable;
    public DocumentStorageReference? Storage { get; set; }
    public string? FilePath { get; set; }
    public object? ParsedDocument { get; set; }
    public List<ExtractionError> Errors { get; } = [];
}
