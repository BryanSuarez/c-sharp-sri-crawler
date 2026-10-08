using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Serialization;
namespace DescagaCompronanteSRI.Models.Enums;

[JsonConverter(typeof(ApiEnumConverter<ExtractionStatus>))]
public enum ExtractionStatus { Completed, Partial, Failed, NoDocuments }

[JsonConverter(typeof(ApiEnumConverter<DocumentDownloadStatus>))]
public enum DocumentDownloadStatus { Downloaded, Failed }

[JsonConverter(typeof(ApiEnumConverter<DocumentParseStatus>))]
public enum DocumentParseStatus { Parsed, Failed, Unsupported, NotApplicable }

[JsonConverter(typeof(ApiEnumConverter<ExtractionErrorCode>))]
public enum ExtractionErrorCode
{
    LoginFailed, PortalAccessFailed, QueryFailed, RowReadFailed,
    DownloadFailed, ParsingFailed, UnsupportedDocumentType, StorageFailed, UnexpectedError,
    PaginationNavigationFailed, RepeatedPage, DuplicateDocument, PaginationStateUnknown,
    PaginationInconsistent, PaginationLimitReached,
    InvalidMetadata, InvalidDocument, UnsupportedDocumentVersion, ValidationFailed, DocumentIdentityMismatch, InputTooLarge
}

[JsonConverter(typeof(ApiEnumConverter<PaginationStatus>))]
public enum PaginationStatus { NotStarted, Completed, Incomplete }

[JsonConverter(typeof(ApiEnumConverter<DocumentValidationStatus>))]
public enum DocumentValidationStatus { NotChecked, Valid, Invalid, Unsupported, Failed }
[JsonConverter(typeof(ApiEnumConverter<DocumentStorageStatus>))]
public enum DocumentStorageStatus { NotAttempted, Stored, Failed }
[JsonConverter(typeof(ApiEnumConverter<DocumentIdentityStatus>))]
public enum DocumentIdentityStatus { NotVerified, Verified, Mismatch }
[JsonConverter(typeof(ApiEnumConverter<MetadataParseStatus>))]
public enum MetadataParseStatus { NotChecked, Parsed, Partial, Failed }
