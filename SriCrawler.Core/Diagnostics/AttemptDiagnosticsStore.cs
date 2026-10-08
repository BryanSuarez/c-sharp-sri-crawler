using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Diagnostics;

public static class AttemptDiagnosticsStore
{
    public static Task<int> SaveAsync(CrawlerDbContext db, Guid extractionId, Guid attemptId,
        AttemptDiagnosticsSnapshot snapshot, CancellationToken token)
    {
        var json = ExtractionJobs.Serialize(snapshot);
        return db.Attempts.Where(x => x.Id == attemptId && x.ExtractionId == extractionId &&
            x.DiagnosticsSequence < snapshot.Sequence && db.Extractions.Any(e => e.Id == extractionId &&
                e.ActiveAttemptId == attemptId && e.JobStatus == JobStatus.Running))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DiagnosticsJson, json)
                .SetProperty(x => x.DiagnosticsVersion, snapshot.Version)
                .SetProperty(x => x.DiagnosticsSequence, snapshot.Sequence)
                .SetProperty(x => x.DiagnosticsUpdatedAt, snapshot.CapturedAt), token);
    }

    public static void MarkInterrupted(ExtractionAttempt attempt, DateTimeOffset recoveredAt)
    {
        if (attempt.DiagnosticsJson is not null)
        {
            var snapshot = ExtractionJobs.Deserialize<AttemptDiagnosticsSnapshot>(attempt.DiagnosticsJson);
            attempt.DiagnosticsJson = ExtractionJobs.Serialize(snapshot with { Coverage = DiagnosticsCoverage.Incomplete });
        }
        attempt.FinishedAt ??= recoveredAt;
        attempt.ErrorCode ??= "recoveryInterrupted";
    }

    public static async Task<ExtractionTimings> SummaryAsync(CrawlerDbContext db, ExtractionRecord record, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        double active = 0;
        var observed = 0; var missing = false; var incomplete = false; var recording = false;
        DateTimeOffset? updated = null;
        var stages = new Dictionary<string, TimingAggregate>(StringComparer.Ordinal);
        var operations = new Dictionary<string, TimingAggregate>(StringComparer.Ordinal);
        await foreach (var attempt in db.Attempts.AsNoTracking().Where(x => x.ExtractionId == record.Id)
            .Select(x => x.DiagnosticsJson).AsAsyncEnumerable().WithCancellation(token))
        {
            if (attempt is null) { missing = true; continue; }
            var snapshot = ExtractionJobs.Deserialize<AttemptDiagnosticsSnapshot>(attempt);
            observed++; active += snapshot.ActiveDurationMs;
            updated = updated is null || snapshot.CapturedAt > updated ? snapshot.CapturedAt : updated;
            incomplete |= snapshot.Coverage == DiagnosticsCoverage.Incomplete;
            recording |= snapshot.Coverage == DiagnosticsCoverage.Recording;
            Merge(stages, snapshot.Stages); Merge(operations, snapshot.Operations);
        }
        var coverage = observed == 0 ? DiagnosticsCoverage.NotAvailable : missing || incomplete ? DiagnosticsCoverage.Incomplete
            : recording ? DiagnosticsCoverage.Recording : DiagnosticsCoverage.Complete;
        return new(Math.Max(0, ((record.FinishedAt ?? now) - record.CreatedAt).TotalMilliseconds),
            Math.Max(0, ((record.StartedAt ?? record.FinishedAt ?? now) - record.CreatedAt).TotalMilliseconds),
            observed == 0 ? null : active, coverage, updated,
            observed == 0 ? null : stages.Values.OrderBy(x => x.Name).ToArray(),
            observed == 0 ? null : operations.Values.OrderBy(x => x.Name).ToArray());
    }
    private static void Merge(Dictionary<string, TimingAggregate> target, IReadOnlyList<TimingAggregate> source)
    {
        foreach (var value in source)
        {
            if (!target.TryGetValue(value.Name, out var previous)) { target[value.Name] = value; continue; }
            var count = previous.Count + value.Count; var total = previous.TotalDurationMs + value.TotalDurationMs;
            target[value.Name] = new(value.Name, count, total, count == 0 ? 0 : total / count,
                Math.Max(previous.MaxDurationMs, value.MaxDurationMs), previous.SucceededCount + value.SucceededCount,
                previous.FailedCount + value.FailedCount, previous.CancelledCount + value.CancelledCount,
                previous.UnsupportedCount + value.UnsupportedCount, previous.InProgressDurationMs + value.InProgressDurationMs);
        }
    }
}
