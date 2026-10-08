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
    public DocumentAcquisitionSource AcquisitionSource { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long? ReusedFileId { get; set; }
    public DocumentValidationResult Validation { get; set; } = new();
    public DocumentStorageStatus StorageStatus { get; set; } = DocumentStorageStatus.NotAttempted;
    public MetadataParseStatus MetadataParseStatus => Metadata?.MetadataParseStatus ?? MetadataParseStatus.NotChecked;
    public DocumentParseStatus ParseStatus { get; set; } = DocumentParseStatus.NotApplicable;
    public DocumentStorageReference? Storage { get; set; }
    public string? FilePath { get; set; }
    public object? ParsedDocument { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? XmlHash { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? SourceXml { get; set; }
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public List<ExtractionError> Errors { get; } = [];
}
