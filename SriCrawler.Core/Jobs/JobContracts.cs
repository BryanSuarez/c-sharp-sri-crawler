using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DescagaCompronanteSRI.Serialization;
using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;

namespace DescagaCompronanteSRI.Jobs;

[JsonConverter(typeof(ApiEnumConverter<ExecutionMode>))]
public enum ExecutionMode { Async, Sync }
[JsonConverter(typeof(ApiEnumConverter<JobStatus>))]
public enum JobStatus { Queued, Running, Retrying, Completed, Failed }
[JsonConverter(typeof(ApiEnumConverter<ExtractionStage>))]
public enum ExtractionStage { Queued, Login, PortalAccess, Query, Processing, Finished }

public sealed class ExtractionJobOptions
{
    public string ConnectionString { get; set; } = "";
    public string EncryptionKey { get; set; } = "";
    [Range(1, 3600)] public int SyncWaitSeconds { get; set; } = 120;
    [Range(1, 64)] public int WorkerCount { get; set; } = 1;
    [Range(1, 48)] public int AttemptTimeoutHours { get; set; } = 6;
    [Range(1, 168)] public int CredentialLifetimeHours { get; set; } = 24;
    [Range(1, 60)] public int DispatchIntervalSeconds { get; set; } = 5;
    public bool DashboardEnabled { get; set; }
    public string ParserUrl { get; set; } = "http://localhost:3000";
    [Range(1, 300)] public int ParserTimeoutSeconds { get; set; } = 30;
}

public sealed record ExtractionAccepted(Guid ExtractionId, string CompanyId, string TaxpayerId,
    JobStatus JobStatus, string StatusUrl, string DocumentsUrl, string ErrorsUrl);
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record ExtractionSummary(Guid ExtractionId, string CompanyId, string TaxpayerId,
    JobStatus JobStatus, ExtractionStatus? ExtractionStatus, ExtractionStage Stage, int Attempts,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt,
    DateTimeOffset? LastActivityAt, int DiscoveredCount, int DownloadedCount, int FailedCount,
    int ConversionFailedCount, int ConversionUnsupportedCount, PaginationProgress Pagination,
    int ValidationIssueCount = 0, int MetadataIssueCount = 0);
public sealed record JsonConversion(DocumentParseStatus Status, JsonElement? DocumentJson = null,
    string? ParserName = null, string? ParserVersion = null, string? ErrorCode = null)
{
    public const int SchemaVersion = 1;
}

public interface ISriDocumentJsonParser
{
    Task<JsonConversion> ParseAsync(string xml, DocumentType type, string accessKey, CancellationToken token);
}
public interface IExtractionProgress
{
    Task StageAsync(ExtractionStage stage, CancellationToken token);
    Task<ReceivedDocumentResponse?> FindSavedAsync(string accessKey, int page, int row, CancellationToken token);
    Task SaveAsync(ReceivedDocumentResponse document, CancellationToken token);
    Task CheckpointAsync(ReceivedDocumentsResponse response, CancellationToken token);
}
public interface IReceivedExtractionRunner
{
    Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token);
}
public interface IExtractionJobs
{
    Task<ExtractionAccepted> AcceptAsync(ReceivedDocumentsQuery query, Guid? clientRequestId, CancellationToken token);
    Task<ExtractionSummary?> GetAsync(Guid id, string companyId, CancellationToken token);
    Task<CursorPage<JsonElement>?> DocumentsAsync(Guid id, string companyId, long cursor, int limit, CancellationToken token);
    Task<JsonElement?> DocumentAsync(Guid id, string companyId, long documentId, CancellationToken token);
    Task<CursorPage<JsonElement>?> ErrorsAsync(Guid id, string companyId, long cursor, int limit, CancellationToken token);
    Task<ReceivedDocumentsResponse?> ResultAsync(Guid id, string companyId, CancellationToken token);
}
public sealed class IdempotencyConflictException : Exception;
public sealed class PersistenceUnavailableException : Exception;
