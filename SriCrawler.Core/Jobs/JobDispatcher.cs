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
            { logger.LogWarning("Job dispatch temporarily unavailable ({ErrorType}).", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
        }
    }
    public async Task DispatchOnceAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var pending = await db.Dispatches.FromSqlRaw("SELECT * FROM crawler.dispatches WHERE \"DispatchedAt\" IS NULL ORDER BY \"Id\" LIMIT 20 FOR UPDATE SKIP LOCKED").ToListAsync(token);
            foreach (var item in pending)
            {
                item.HangfireJobId = jobs.Enqueue<ExtractionWorker>(x => x.ExecuteAsync(item.ExtractionId, CancellationToken.None));
                item.DispatchedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        db.ChangeTracker.Clear();
        var now = DateTimeOffset.UtcNow;
        var stale = await db.Extractions.Where(x => (x.JobStatus == JobStatus.Running && x.LastActivityAt < now.AddMinutes(-2)) || (x.JobStatus == JobStatus.Queued && x.CreatedAt < now.AddMinutes(-2) && (x.LastActivityAt == null || x.LastActivityAt < now.AddMinutes(-2)))).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(20).ToListAsync(token);
        foreach (var job in stale)
        {
            jobs.Enqueue<ExtractionWorker>(x => x.ExecuteAsync(job.Id, CancellationToken.None));
            // Active account locks still guard paused/live workers. Avoid dispatching on every poll.
            job.LastActivityAt = now;
        }
        var expired = await db.Extractions.Where(x => x.ExpiresAt <= now && x.EncryptedPassword != null).OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Take(100).ToListAsync(token);
        foreach (var job in expired)
        {
            job.EncryptedPassword = null;
            if (job.JobStatus is JobStatus.Completed or JobStatus.Failed) continue;
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
    }
}
