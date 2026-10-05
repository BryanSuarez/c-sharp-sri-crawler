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
    PaginationInconsistent, PaginationLimitReached
}

[JsonConverter(typeof(ApiEnumConverter<PaginationStatus>))]
public enum PaginationStatus { NotStarted, Completed, Incomplete }
