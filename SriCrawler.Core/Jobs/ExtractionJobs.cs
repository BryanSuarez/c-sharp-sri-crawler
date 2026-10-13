using DescagaCompronanteSRI.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DescagaCompronanteSRI.Jobs;

public sealed partial class ExtractionJobs(CrawlerDbContext db, CredentialCipher cipher,
    IOptions<ExtractionJobOptions> options, ILogger<ExtractionJobs>? logger = null, TimeProvider? clock = null) : IExtractionJobs
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Deserialize<T>(string value)
    {
        var result = JsonSerializer.Deserialize<T>(value, Json)!;
        if (result is ReceivedDocumentResponse { DownloadStatus: DocumentDownloadStatus.Downloaded,
            StorageStatus: DocumentStorageStatus.NotAttempted, Storage: not null } response)
        {
            // Historical snapshots predate storageStatus. Infer only for a missing field;
            // new results must report storage independently and explicitly.
            using var historical = JsonDocument.Parse(value);
            if (!historical.RootElement.TryGetProperty("storageStatus", out _)) response.StorageStatus = DocumentStorageStatus.Stored;
        }
        if (result is ReceivedDocumentResponse legacy)
        {
            using var snapshot = JsonDocument.Parse(value);
            if (!snapshot.RootElement.TryGetProperty("acquisitionSource", out _) && legacy.DownloadStatus == DocumentDownloadStatus.Downloaded)
                legacy.AcquisitionSource = DocumentAcquisitionSource.Downloaded;
        }
        return result;
    }

    public async Task<ExtractionCancellation?> CancelAsync(Guid id, string companyId, Guid? requestedBy, CancellationToken token)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var record = await ExtractionLifecycle.LockAsync(db, id, token, companyId);
            if (record is null) return null;
            if (record.JobStatus is JobStatus.Completed or JobStatus.Failed) throw new ExtractionAlreadyFinishedException();
            var changed = record.JobStatus is not (JobStatus.CancellationRequested or JobStatus.Cancelled);
            if (changed)
            {
                var wasRunning = record.JobStatus == JobStatus.Running;
                record.CancellationRequestedAt = ExtractionLifecycle.Timestamp((clock ?? TimeProvider.System).GetUtcNow());
                record.CancellationRequestedBy = requestedBy;
                record.EncryptedPassword = null;
                record.JobStatus = JobStatus.CancellationRequested;
                if (!wasRunning) await ExtractionLifecycle.CancelAsync(db, record, record.CancellationRequestedAt.Value, token);
                else await db.SaveChangesAsync(token);
            }
            await transaction.CommitAsync(token);
            ExtractionDiagnostics.Safely(() => logger?.LogInformation(new EventId(1420, "ExtractionCancellationRequested"),
                "Cancellation for extraction {extractionId}, company {companyId}: {jobStatus}, repeated {repeated}.",
                id, companyId, record.JobStatus, !changed));
            return new(record.Id, record.JobStatus, record.CancellationRequestedAt, record.CancelledAt);
        }
        catch (Exception e) when (e is NpgsqlException or DbUpdateException) { throw new PersistenceUnavailableException(); }
    }

    public Task<ExtractionAccepted> AcceptAsync(ReceivedDocumentsQuery query, Guid? requestId, CancellationToken token) =>
        AcceptCoreAsync(query, requestId, token);

    private async Task<ExtractionAccepted> AcceptCoreAsync(ReceivedDocumentsQuery query, Guid? requestId, CancellationToken token,
        Guid? sourceId = null, SelectedRetryMode? mode = null, long[]? selectedIds = null)
    {
        var safeQuery = query with { Password = "" };
        var queryJson = Serialize(safeQuery);
        var identity = sourceId is null ? queryJson : Serialize(new { sourceId, mode, selectedIds });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // Serialize quota checks and acceptance across API replicas for this company only.
            var admissionKey = CompanyWorkload.LockKey("sri-company-admission:" + query.CompanyId);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({admissionKey})", token);
            if (requestId is not null)
            {
                var previous = await db.Extractions.AsNoTracking().SingleOrDefaultAsync(
                    x => x.CompanyId == query.CompanyId && x.ClientRequestId == requestId, token);
                if (previous is not null) return Match(previous, fingerprint);
            }
            var source = sourceId is Guid parent ? await ValidateSelectionAsync(parent, query.CompanyId, mode!.Value, selectedIds!, token) : null;
            var id = Guid.NewGuid();
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            await CompanyWorkload.CheckAdmissionAsync(db, query.CompanyId, options.Value, now, token);
            var extraction = new ExtractionRecord
            {
                Id = id, CompanyId = query.CompanyId, TaxpayerId = query.User,
                BusinessName = source?.BusinessName ?? "",
                SourceExtractionId = sourceId, RetryMode = mode, RetrySelectionJson = selectedIds is null ? null : Serialize(selectedIds),
                DownloadPolicy = query.DownloadPolicy, ClientRequestId = requestId, Fingerprint = fingerprint, QueryJson = queryJson,
                EncryptedPassword = mode == SelectedRetryMode.Parsing ? null : cipher.Encrypt(query.Password, id), CreatedAt = now,
                ExpiresAt = now.AddHours(options.Value.CredentialLifetimeHours),
                JobStatus = JobStatus.Queued, Stage = ExtractionStage.Queued,
                PaginationJson = Serialize(new PaginationProgress())
            };
            db.Extractions.Add(extraction);
            db.Dispatches.Add(new() { Id = Guid.NewGuid(), ExtractionId = id });
            // Quota check, extraction and outbox commit together under the company lock.
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" } && requestId is not null)
            {
                db.ChangeTracker.Clear();
                var previous = await db.Extractions.AsNoTracking().SingleAsync(
                    x => x.CompanyId == query.CompanyId && x.ClientRequestId == requestId, token);
                return Match(previous, fingerprint);
            }
            await transaction.CommitAsync(token);
            return LogAccepted(extraction, false);
        }
        catch (WorkloadLimitExceededException e)
        {
            ExtractionDiagnostics.Safely(() => logger?.LogWarning(new EventId(1410, "CompanyAdmissionRejected"),
                "Extraction admission rejected for company {companyId}: {code}, limit {limit}, current {current}.",
                query.CompanyId, e.Result.Code, e.Result.Limit, e.Result.Current));
            throw;
        }
        catch (Exception e) when (e is NpgsqlException or DbUpdateException)
        { throw new PersistenceUnavailableException(); }
    }
    private ExtractionAccepted Match(ExtractionRecord record, string fingerprint) =>
        record.Fingerprint == fingerprint ? LogAccepted(record, true) : throw new IdempotencyConflictException();
    private ExtractionAccepted LogAccepted(ExtractionRecord record, bool existing)
    {
        if (logger is not null)
        {
            using var scope = ExtractionDiagnostics.SafeScope(logger, new Dictionary<string, object> { ["extractionId"] = record.Id, ["companyId"] = record.CompanyId });
            ExtractionDiagnostics.Event(logger, LogLevel.Information, existing ? DiagnosticEvent.IdempotentMatch : DiagnosticEvent.Accepted, stage: record.Stage);
            if (record.SourceExtractionId is Guid parent)
                ExtractionDiagnostics.Safely(() => logger.LogInformation(new EventId(1430, "SelectedRetryAccepted"),
                    "Selected retry for source {sourceExtractionId}, mode {retryMode}, count {selectedCount}, repeated {repeated}.",
                    parent, record.RetryMode, Deserialize<long[]>(record.RetrySelectionJson!).Length, existing));
        }
        return Accepted(record);
    }
    private static ExtractionAccepted Accepted(ExtractionRecord record)
    {
        var path = $"/api/received-documents/extractions/{record.Id}";
        var query = $"?companyId={Uri.EscapeDataString(record.CompanyId)}";
        return new(record.Id, record.CompanyId, record.TaxpayerId, record.JobStatus,
            path + query, path + "/documents" + query, path + "/errors" + query);
    }
    private Task<ExtractionRecord?> Find(Guid id, string company, CancellationToken token) =>
        db.Extractions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company, token);
    private IQueryable<ExtractionDocument> CurrentResults(ExtractionRecord record) => db.Results.AsNoTracking()
        .Where(x => x.ExtractionId == record.Id && (x.DocumentId != null || x.SeenAttemptId == record.ActiveAttemptId));

    public async Task<ExtractionSummary?> GetAsync(Guid id, string companyId, CancellationToken token)
    {
        var e = await Find(id, companyId, token);
        if (e is null) return null;
        var results = CurrentResults(e);
        // Read all counters in one database snapshot while the worker is adding results.
        var counts = await results.GroupBy(x => x.ExtractionId).Select(g => new
        {
            Reused = g.Count(x => x.DownloadStatus == DocumentDownloadStatus.Downloaded && x.AcquisitionSource == DocumentAcquisitionSource.Reused),
            Total = g.Count(), Saved = g.Count(x => x.DownloadStatus == DocumentDownloadStatus.Downloaded),
            ConversionFailed = g.Count(x => x.JsonStatus == DocumentParseStatus.Failed),
            Unsupported = g.Count(x => x.JsonStatus == DocumentParseStatus.Unsupported),
            ValidationIssues = g.Count(x => x.ValidationStatus == DocumentValidationStatus.Invalid || x.ValidationStatus == DocumentValidationStatus.Unsupported || x.ValidationStatus == DocumentValidationStatus.Failed),
            MetadataIssues = g.Count(x => x.MetadataParseStatus == MetadataParseStatus.Partial || x.MetadataParseStatus == MetadataParseStatus.Failed)
        }).SingleOrDefaultAsync(token);
        var count = counts?.Total ?? 0;
        var saved = counts?.Saved ?? 0;
        return new(e.Id, e.CompanyId, e.TaxpayerId, e.JobStatus, e.ExtractionStatus, e.Stage, e.AttemptCount,
            e.CreatedAt, e.StartedAt, e.FinishedAt, e.LastActivityAt, count, saved, count - saved,
            counts?.ConversionFailed ?? 0, counts?.Unsupported ?? 0, Deserialize<PaginationProgress>(e.PaginationJson), counts?.ValidationIssues ?? 0, counts?.MetadataIssues ?? 0, saved - (counts?.Reused ?? 0), counts?.Reused ?? 0, await AttemptDiagnosticsStore.SummaryAsync(db, e, token), e.CancellationRequestedAt, e.CancelledAt, e.SourceExtractionId is Guid parent ? new SelectedRetryInfo(parent, e.RetryMode!.Value, Deserialize<long[]>(e.RetrySelectionJson!).Length) : null);
    }
    public async Task<CursorPage<ExtractionAttemptSummary>?> AttemptsAsync(Guid id, string companyId, long cursor, int limit, CancellationToken token)
    {
        if (await Find(id, companyId, token) is null) return null;
        var attempts = await db.Attempts.AsNoTracking().Where(x => x.ExtractionId == id && x.Number > cursor)
            .OrderBy(x => x.Number).Take(limit + 1).ToListAsync(token);
        var items = attempts.Take(limit).Select(x => new ExtractionAttemptSummary(x.Id, x.Number, x.StartedAt,
            x.FinishedAt, x.ErrorCode, x.DiagnosticsJson is null ? null : Deserialize<AttemptDiagnosticsSnapshot>(x.DiagnosticsJson),
            x.DiagnosticsJson is null ? DiagnosticsCoverage.NotAvailable : Deserialize<AttemptDiagnosticsSnapshot>(x.DiagnosticsJson).Coverage)).ToArray();
        return new(items, attempts.Count > limit ? items[^1].Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }
    public async Task<CursorPage<JsonElement>?> DocumentsAsync(Guid id, string companyId, long cursor, int limit, CancellationToken token)
    {
        var e = await Find(id, companyId, token);
        if (e is null) return null;
        var rows = await CurrentResults(e).Where(x => x.Id > cursor).OrderBy(x => x.Id).Take(limit + 1).ToListAsync(token);
        var items = rows.Take(limit).Select(x =>
        {
            var response = Deserialize<ReceivedDocumentResponse>(x.ResponseJson);
            return JsonSerializer.SerializeToElement(new
            {
                documentId = x.Id, e.CompanyId, e.TaxpayerId, response.PageNumber, response.RowIndex,
                response.Metadata, response.DownloadFormat, response.DownloadStatus, response.AcquisitionSource, response.ParseStatus,
                jsonParseStatus = x.JsonStatus, response.Validation, response.StorageStatus, response.MetadataParseStatus, response.Storage, response.FilePath, response.Errors
            }, Json);
        }).ToList();
        return new(items, rows.Count > limit ? rows[limit - 1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }
    public async Task<JsonElement?> DocumentAsync(Guid id, string companyId, long documentId, CancellationToken token)
    {
        var e = await Find(id, companyId, token);
        if (e is null) return null;
        var row = await CurrentResults(e).SingleOrDefaultAsync(x => x.Id == documentId, token);
        if (row is null) return null;
        var conversion = row.ConversionId is null ? null : await db.Conversions.AsNoTracking().SingleAsync(x => x.Id == row.ConversionId, token);
        return JsonSerializer.SerializeToElement(new
        {
            documentId = row.Id, e.CompanyId, e.TaxpayerId,
            document = Deserialize<ReceivedDocumentResponse>(row.ResponseJson),
            documentJson = conversion?.DocumentJson is { } json ? JsonDocument.Parse(json).RootElement.Clone() : (JsonElement?)null,
            jsonParseStatus = row.JsonStatus, conversion?.ParserName, conversion?.ParserVersion,
            conversion?.SchemaVersion, conversion?.XmlHash, conversion?.ErrorCode
        }, Json);
    }
    public async Task<CursorPage<JsonElement>?> ErrorsAsync(Guid id, string companyId, long cursor, int limit, CancellationToken token)
    {
        if (await Find(id, companyId, token) is null) return null;
        var rows = await db.Failures.AsNoTracking().Where(x => x.ExtractionId == id && x.Id > cursor)
            .OrderBy(x => x.Id).Take(limit + 1).ToListAsync(token);
        return new(rows.Take(limit).Select(x => JsonSerializer.SerializeToElement(new
        {
            errorId = x.Id, x.AttemptId, x.CreatedAt, error = JsonDocument.Parse(x.ErrorJson).RootElement.Clone()
        }, Json)).ToList(), rows.Count > limit ? rows[limit - 1].Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }
    public async Task<ReceivedDocumentsResponse?> ResultAsync(Guid id, string companyId, CancellationToken token)
    {
        var e = await Find(id, companyId, token);
        if (e is null || !ExtractionLifecycle.IsTerminal(e.JobStatus)) return null;
        var result = new ReceivedDocumentsResponse { ExtractionId = id, CompanyId = e.CompanyId,
            JobStatus = e.JobStatus, CancellationRequestedAt = e.CancellationRequestedAt, CancelledAt = e.CancelledAt,
            TaxpayerId = e.TaxpayerId, BusinessName = e.BusinessName, QuerySucceeded = e.QuerySucceeded,
            Timings = await AttemptDiagnosticsStore.SummaryAsync(db, e, token),
            Status = e.ExtractionStatus ?? ExtractionStatus.Failed };
        var progress = Deserialize<PaginationProgress>(e.PaginationJson);
        result.Pagination.Status = progress.Status;
        result.Pagination.PagesProcessed = progress.PagesProcessed;
        result.Pagination.ReportedTotalCount = progress.ReportedTotalCount;
        result.Pagination.DuplicateCount = progress.DuplicateCount;
        foreach (var row in await CurrentResults(e).OrderBy(x => x.Id).ToListAsync(token))
            result.Documents.Add(Deserialize<ReceivedDocumentResponse>(row.ResponseJson));
        foreach (var row in await db.Failures.AsNoTracking().Where(x => x.ExtractionId == id && x.AttemptId == e.ActiveAttemptId).OrderBy(x => x.Id).ToListAsync(token))
        {
            try { result.Errors.Add(Deserialize<ExtractionError>(row.ErrorJson)); }
            catch (JsonException) { result.Errors.Add(new(ExtractionErrorCode.UnexpectedError, "Extraction execution or JSON conversion failed. See the paginated errors endpoint.")); }
        }
        return result;
    }
}
