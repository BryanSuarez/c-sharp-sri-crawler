using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DescagaCompronanteSRI.Jobs;

public static class ExtractionLifecycle
{
    public static DateTimeOffset Timestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    public static bool IsTerminal(JobStatus status) => status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;

    // Call only inside a short transaction. Never hold this lock while talking to the portal.
    public static async Task<ExtractionRecord?> LockAsync(CrawlerDbContext db, Guid id, CancellationToken token, string? company = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("An extraction lock requires a transaction.");
        var rows = company is null
            ? db.Extractions.FromSqlInterpolated($"SELECT * FROM crawler.extractions WHERE \"Id\" = {id} FOR UPDATE")
            : db.Extractions.FromSqlInterpolated($"SELECT * FROM crawler.extractions WHERE \"Id\" = {id} AND \"CompanyId\" = {company} FOR UPDATE");
        var record = (await rows.ToListAsync(token)).SingleOrDefault();
        if (record is not null) await db.Entry(record).ReloadAsync(token);
        return record;
    }

    public static async Task<bool> TryLockTaxpayerAsync(NpgsqlConnection connection, string taxpayer, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", CompanyWorkload.LockKey("sri-taxpayer:" + taxpayer));
        return Equals(await command.ExecuteScalarAsync(token), true);
    }

    // The caller owns the row lock and, for an active attempt, the taxpayer session lock.
    public static async Task CancelAsync(CrawlerDbContext db, ExtractionRecord record, DateTimeOffset now, CancellationToken token)
    {
        now = Timestamp(now);
        if (record.JobStatus == JobStatus.Cancelled) return;
        if (record.JobStatus != JobStatus.CancellationRequested) throw new InvalidOperationException("Cancellation was not requested.");
        if (record.ActiveAttemptId is Guid attemptId)
        {
            var attempt = await db.Attempts.SingleAsync(x => x.Id == attemptId, token);
            if (attempt.FinishedAt is null)
            {
                if (ExtractionDiagnostics.Current is { } diagnostics)
                {
                    diagnostics.Finish(DiagnosticsCoverage.Incomplete, DiagnosticOutcome.Cancelled);
                    await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attemptId, diagnostics.Snapshot(), token);
                    await db.Entry(attempt).ReloadAsync(token);
                }
                AttemptDiagnosticsStore.MarkInterrupted(attempt, now);
                attempt.ErrorCode = "extractionCancelled";
            }
        }
        record.JobStatus = JobStatus.Cancelled;
        record.CancelledAt = record.FinishedAt = now;
        record.Stage = ExtractionStage.Finished;
        record.EncryptedPassword = null;
        record.ExtractionStatus = await db.Results.AnyAsync(x => x.ExtractionId == record.Id && x.DownloadStatus == DocumentDownloadStatus.Downloaded, token)
            ? ExtractionStatus.Partial : ExtractionStatus.Failed;
        var pagination = ExtractionJobs.Deserialize<PaginationProgress>(record.PaginationJson);
        if (record.ActiveAttemptId is not null) pagination.Status = PaginationStatus.Incomplete;
        record.PaginationJson = ExtractionJobs.Serialize(pagination);
        db.Failures.Add(new() { ExtractionId = record.Id, AttemptId = record.ActiveAttemptId, CreatedAt = now,
            ErrorJson = ExtractionJobs.Serialize(new ExtractionError(ExtractionErrorCode.ExtractionCancelled, "Extraction cancelled by the company; confirmed documents were preserved.")) });
        await db.SaveChangesAsync(token);
    }
}
