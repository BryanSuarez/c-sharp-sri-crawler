using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Jobs;

public sealed class JobDispatcher(IServiceScopeFactory scopes, IOptions<ExtractionJobOptions> options,
    ILogger<JobDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchOnceAsync(stoppingToken); }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            { ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.DiagnosticsUnavailable, code: "dispatchUnavailable", errorType: e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
        }
    }
    public async Task DispatchOnceAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var dispatchLock = CompanyWorkload.LockKey("sri-fair-dispatcher");
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({dispatchLock}) AS \"Value\"").SingleAsync(token)) return;
        var now = DateTimeOffset.UtcNow;
        var reserved = from dispatch in db.Dispatches
            join extraction in db.Extractions on dispatch.ExtractionId equals extraction.Id
            where extraction.JobStatus != JobStatus.Completed && extraction.JobStatus != JobStatus.Failed && extraction.ExpiresAt > now
                && (dispatch.DispatchedAt != null || extraction.JobStatus == JobStatus.Running)
            select extraction;
        var available = Math.Max(0, options.Value.MaxDispatchedExtractions - await reserved.CountAsync(token));
        var published = new List<(Guid Id, string Company)>();
        for (var slot = 0; slot < available; slot++)
        {
            // Oldest turn per company, then FIFO within that company. Leave the rest in the outbox.
            var next = await (from dispatch in db.Dispatches
                join extraction in db.Extractions on dispatch.ExtractionId equals extraction.Id
                where dispatch.DispatchedAt == null && extraction.JobStatus == JobStatus.Queued
                    && extraction.ExpiresAt > now && extraction.EncryptedPassword != null
                    && reserved.Count(x => x.CompanyId == extraction.CompanyId) < options.Value.MaxConcurrentPerCompany
                let lastTurn = (from previous in db.Dispatches
                    join owner in db.Extractions on previous.ExtractionId equals owner.Id
                    where owner.CompanyId == extraction.CompanyId
                    select previous.DispatchedAt).Max()
                orderby lastTurn ?? DateTimeOffset.MinValue, extraction.CreatedAt, extraction.Id
                select new { Dispatch = dispatch, extraction.CompanyId }).FirstOrDefaultAsync(token);
            if (next is null) break;
            var item = next.Dispatch;
            item.HangfireJobId = jobs.Enqueue<ExtractionWorker>(x => x.ExecuteAsync(item.ExtractionId, CancellationToken.None));
            item.DispatchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            published.Add((item.ExtractionId, next.CompanyId));
        }
        db.ChangeTracker.Clear();
        var stale = await db.Extractions.Where(x => x.ExpiresAt > now && ((x.JobStatus == JobStatus.Running && x.LastActivityAt < now.AddMinutes(-2)) || (x.JobStatus == JobStatus.Queued && x.CreatedAt < now.AddMinutes(-2) && (x.LastActivityAt == null || x.LastActivityAt < now.AddMinutes(-2))
            && db.Dispatches.Any(d => d.ExtractionId == x.Id && d.DispatchedAt != null && d.DispatchedAt < now.AddMinutes(-2)))))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(20).ToListAsync(token);
        foreach (var job in stale)
        {
            using var logScope = ExtractionDiagnostics.SafeScope(logger, new Dictionary<string, object> { ["extractionId"] = job.Id, ["companyId"] = job.CompanyId });
            ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.DispatchRecovered, code: "staleExecution");
            jobs.Enqueue<ExtractionWorker>(x => x.ExecuteAsync(job.Id, CancellationToken.None));
            // Active account locks still guard paused/live workers. Avoid dispatching on every poll.
            job.LastActivityAt = now;
        }
        var expired = await db.Extractions.Where(x => x.ExpiresAt <= now && x.EncryptedPassword != null).OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).ToListAsync(token);
        foreach (var job in expired)
        {
            job.EncryptedPassword = null;
            if (job.JobStatus is JobStatus.Completed or JobStatus.Failed) continue;
            var activeAttempt = job.ActiveAttemptId is Guid attemptId ? await db.Attempts.SingleOrDefaultAsync(x => x.Id == attemptId, token) : null;
            if (activeAttempt is not null && activeAttempt.FinishedAt is null) AttemptDiagnosticsStore.MarkInterrupted(activeAttempt, now);
            job.JobStatus = JobStatus.Failed;
            job.ExtractionStatus = await db.Results.AnyAsync(x => x.ExtractionId == job.Id && x.DownloadStatus == Models.Enums.DocumentDownloadStatus.Downloaded, token)
                ? Models.Enums.ExtractionStatus.Partial : Models.Enums.ExtractionStatus.Failed;
            job.Stage = ExtractionStage.Finished;
            job.FinishedAt = now;
            var pagination = ExtractionJobs.Deserialize<Models.Responses.PaginationProgress>(job.PaginationJson);
            pagination.Status = Models.Enums.PaginationStatus.Incomplete;
            job.PaginationJson = ExtractionJobs.Serialize(pagination);
            db.Failures.Add(new() { ExtractionId = job.Id, AttemptId = job.ActiveAttemptId, CreatedAt = now,
                ErrorJson = ExtractionJobs.Serialize(new { code = "credentialsExpired", message = "Extraction credentials have expired." }) });
        }
        await db.SaveChangesAsync(token);
        // Retry queue publication failures without needing the API or Laravel to be running.
        var orphanedRetries = await db.Extractions.AsNoTracking().Where(x => x.JobStatus == JobStatus.Retrying && x.LastActivityAt < now.AddMinutes(-3)).OrderBy(x => x.LastActivityAt).ThenBy(x => x.Id).Take(20).ToListAsync(token);
        foreach (var job in orphanedRetries)
        {
            jobs.Enqueue<ExtractionWorker>(x => x.ExecuteAsync(job.Id, CancellationToken.None));
            await db.Extractions.Where(x => x.Id == job.Id && x.JobStatus == JobStatus.Retrying)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, now), token);
        }
        await transaction.CommitAsync(token);
        foreach (var item in published)
        {
            using var logScope = ExtractionDiagnostics.SafeScope(logger, new Dictionary<string, object> { ["extractionId"] = item.Id, ["companyId"] = item.Company });
            ExtractionDiagnostics.Event(logger, LogLevel.Information, DiagnosticEvent.Dispatched);
        }
    }
}
