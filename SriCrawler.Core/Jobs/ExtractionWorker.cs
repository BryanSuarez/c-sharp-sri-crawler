using System.Security.Cryptography;
using System.Text;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DescagaCompronanteSRI.Jobs;

public sealed class ExtractionWorker(IDbContextFactory<CrawlerDbContext> factory, IReceivedExtractionRunner runner,
    CredentialCipher cipher, ISriDocumentJsonParser parser, IDocumentStorage storage,
    IBackgroundJobClient jobs, IOptions<ExtractionJobOptions> options, ILogger<ExtractionWorker> logger, IDocumentValidator validator, IOptions<DescagaCompronanteSRI.Validation.DocumentValidationOptions> validationOptions)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(Guid extractionId, CancellationToken cancellationToken)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["ExtractionId"] = extractionId });
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var record = await db.Extractions.SingleOrDefaultAsync(x => x.Id == extractionId, cancellationToken);
        if (record is null || record.JobStatus is JobStatus.Completed or JobStatus.Failed) return;
        var connectionOptions = new NpgsqlConnectionStringBuilder(options.Value.ConnectionString) { Pooling = false, KeepAlive = 10, CommandTimeout = 10 };
        await using var connection = new NpgsqlConnection(connectionOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var lockKey = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("sri-taxpayer:" + record.TaxpayerId)), 0);
        await using (var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection))
        {
            acquire.Parameters.AddWithValue("key", lockKey);
            if (!Equals(await acquire.ExecuteScalarAsync(cancellationToken), true))
            {
                if (record.JobStatus != JobStatus.Running)
                    jobs.Schedule<ExtractionWorker>(x => x.ExecuteAsync(extractionId, CancellationToken.None), TimeSpan.FromSeconds(30));
                return;
            }
        }
        // Refresh after acquiring the account lock: a previous delivery may already have finished.
        await db.Entry(record).ReloadAsync(cancellationToken);
        if (record.JobStatus is JobStatus.Completed or JobStatus.Failed) return;
        var transientFailures = await db.Attempts.CountAsync(x => x.ExtractionId == extractionId && x.ErrorCode == "interrupted", cancellationToken);
        if (record.ExpiresAt <= DateTimeOffset.UtcNow || record.EncryptedPassword is null || transientFailures >= 3)
        {
            await FailAsync(record, "executionExpired", cancellationToken);
            return;
        }
        var query = ExtractionJobs.Deserialize<ReceivedDocumentsQuery>(record.QueryJson) with { Password = cipher.Decrypt(record.EncryptedPassword, record.Id) };
        var attemptId = Guid.NewGuid();
        record.AttemptCount++;
        record.ActiveAttemptId = attemptId;
        record.JobStatus = JobStatus.Running;
        record.Stage = ExtractionStage.Login;
        record.StartedAt ??= DateTimeOffset.UtcNow;
        record.LastActivityAt = DateTimeOffset.UtcNow;
        record.PaginationJson = ExtractionJobs.Serialize(new PaginationProgress());
        db.Attempts.Add(new() { Id = attemptId, ExtractionId = extractionId, Number = record.AttemptCount, StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddHours(options.Value.AttemptTimeoutHours);
        execution.CancelAfter(TimeSpan.FromHours(options.Value.AttemptTimeoutHours));
        var heartbeat = HeartbeatAsync(connection, extractionId, attemptId, execution);
        var progress = new PersistentExtractionProgress(factory, parser, storage, extractionId, attemptId, query, validator, validationOptions);
        try
        {
            var response = await runner.RunAsync(query, progress, execution.Token);
            if (await progress.HasUnseenDocumentsAsync(execution.Token))
            {
                response.Pagination.Status = PaginationStatus.Incomplete;
                response.Errors.Add(new(ExtractionErrorCode.PaginationInconsistent, "Documents from an earlier attempt could not be verified."));
            }
            await progress.CheckpointAsync(response, execution.Token);
            await CompleteAsync(extractionId, attemptId, JobStatus.Completed, null, execution.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Hangfire requeues shutdown cancellations. Preserve credentials and stored documents.
            await SetRetryingBestEffortAsync(extractionId, attemptId, "shutdown");
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning("Extraction {ExtractionId} attempt {Attempt} interrupted ({ErrorType}). Stack: {StackTrace}",
                extractionId, record.AttemptCount, e.GetType().Name, e.StackTrace);
            // Never turn an exhausted execution into a successful Hangfire/business result.
            if (transientFailures < 2 && record.ExpiresAt > DateTimeOffset.UtcNow && DateTimeOffset.UtcNow < deadline)
            {
                await SetRetryingBestEffortAsync(extractionId, attemptId);
                jobs.Schedule<ExtractionWorker>(x => x.ExecuteAsync(extractionId, CancellationToken.None),
                    TimeSpan.FromSeconds(transientFailures == 0 ? 30 : 120));
            }
            else await CompleteAsync(extractionId, attemptId, JobStatus.Failed,
                execution.IsCancellationRequested ? "executionInterrupted" : "executionFailed", CancellationToken.None);
        }
        finally
        {
            execution.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }
    private async Task HeartbeatAsync(NpgsqlConnection connection, Guid id, Guid attempt, CancellationTokenSource execution)
    {
        try
        {
            while (!execution.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), execution.Token);
                await using var probe = new NpgsqlCommand("SELECT 1", connection);
                await probe.ExecuteScalarAsync(execution.Token);
                await using var db = await factory.CreateDbContextAsync(execution.Token);
                var updated = await db.Extractions.Where(x => x.Id == id && x.ActiveAttemptId == attempt && x.JobStatus == JobStatus.Running && x.ExpiresAt > DateTimeOffset.UtcNow)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow), execution.Token);
                if (updated == 0) { execution.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch (Exception) { execution.Cancel(); }
    }
    private async Task SetRetryingBestEffortAsync(Guid id, Guid attempt, string code = "interrupted")
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Extractions.Where(x => x.Id == id && x.ActiveAttemptId == attempt && x.JobStatus == JobStatus.Running)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, JobStatus.Retrying));
            await db.Attempts.Where(x => x.Id == attempt).ExecuteUpdateAsync(s => s.SetProperty(x => x.FinishedAt, DateTimeOffset.UtcNow).SetProperty(x => x.ErrorCode, code));
        }
        catch (Exception) { /* The reconciler will recover stale executions when persistence returns. */ }
    }
    private async Task FailAsync(ExtractionRecord record, string code, CancellationToken token)
    {
        record.JobStatus = JobStatus.Failed;
        record.EncryptedPassword = null;
        record.FinishedAt = DateTimeOffset.UtcNow;
        record.Stage = ExtractionStage.Finished;
        var pagination = ExtractionJobs.Deserialize<PaginationProgress>(record.PaginationJson);
        pagination.Status = PaginationStatus.Incomplete;
        record.PaginationJson = ExtractionJobs.Serialize(pagination);
        await using var db = await factory.CreateDbContextAsync(token);
        record.ExtractionStatus = await db.Results.AnyAsync(x => x.ExtractionId == record.Id &&
            x.DownloadStatus == DocumentDownloadStatus.Downloaded, token) ? ExtractionStatus.Partial : ExtractionStatus.Failed;
        db.Attach(record);
        db.Entry(record).State = EntityState.Modified;
        db.Failures.Add(new() { ExtractionId = record.Id, AttemptId = record.ActiveAttemptId, CreatedAt = DateTimeOffset.UtcNow,
            ErrorJson = ExtractionJobs.Serialize(new { code, message = "The extraction can no longer execute." }) });
        await db.SaveChangesAsync(token);
    }
    private async Task CompleteAsync(Guid id, Guid attempt, JobStatus status, string? errorCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var e = await db.Extractions.SingleAsync(x => x.Id == id, token);
        if (e.ActiveAttemptId != attempt || e.JobStatus == JobStatus.Failed) return;
        var rows = db.Results.Where(x => x.ExtractionId == id && (x.DocumentId != null || x.SeenAttemptId == attempt));
        var total = await rows.CountAsync(token);
        var saved = await rows.CountAsync(x => x.DownloadStatus == DocumentDownloadStatus.Downloaded, token);
        var pagination = ExtractionJobs.Deserialize<PaginationProgress>(e.PaginationJson);
        if (status == JobStatus.Failed) pagination.Status = PaginationStatus.Incomplete;
        var complete = pagination.Status == PaginationStatus.Completed;
        e.ExtractionStatus = saved > 0 ? complete && total == saved ? ExtractionStatus.Completed : ExtractionStatus.Partial
            : complete && total == 0 ? ExtractionStatus.NoDocuments : ExtractionStatus.Failed;
        e.PaginationJson = ExtractionJobs.Serialize(pagination);
        e.JobStatus = status;
        e.Stage = ExtractionStage.Finished;
        e.FinishedAt = DateTimeOffset.UtcNow;
        e.EncryptedPassword = null;
        var run = await db.Attempts.SingleAsync(x => x.Id == attempt, token);
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.ErrorCode = errorCode;
        if (errorCode is not null) db.Failures.Add(new() { ExtractionId = id, AttemptId = attempt, CreatedAt = DateTimeOffset.UtcNow,
            ErrorJson = ExtractionJobs.Serialize(new { code = errorCode, message = "Extraction interrupted; stored documents were preserved." }) });
        await db.SaveChangesAsync(token);
    }
}
