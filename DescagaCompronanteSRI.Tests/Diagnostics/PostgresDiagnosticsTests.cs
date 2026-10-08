using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Diagnostics;

[Collection("HangfireHost")]
public sealed class PostgresDiagnosticsTests
{
    private static async Task<CrawlerDbContext> Database()
    {
        var db = new CrawlerDbContext(new DbContextOptionsBuilder<CrawlerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")!).Options);
        await db.Database.MigrateAsync(); return db;
    }
    private static ExtractionJobs Jobs(CrawlerDbContext db)
    {
        var options = Options.Create(new ExtractionJobOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) });
        return new(db, new CredentialCipher(options), options);
    }
    private static async Task<(ExtractionRecord Record, Guid Attempt)> Start(CrawlerDbContext db)
    {
        var company = "diagnostics-" + Guid.NewGuid().ToString("N");
        var accepted = await Jobs(db).AcceptAsync(new ReceivedDocumentsQuery { CompanyId = company, User = "1790012345001",
            Password = "fixture-only", Year = 2026, Month = 5, DocumentType = DocumentType.Invoice, DownloadFormat = DownloadFormat.Xml }, null, default);
        var record = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
        record.JobStatus = JobStatus.Running; record.ActiveAttemptId = Guid.NewGuid(); record.AttemptCount = 1;
        record.StartedAt = DateTimeOffset.UtcNow;
        db.Attempts.Add(new() { Id = record.ActiveAttemptId.Value, ExtractionId = record.Id, Number = 1, StartedAt = record.StartedAt.Value });
        await db.SaveChangesAsync(); return (record, record.ActiveAttemptId.Value);
    }

    [PostgresFact]
    public async Task AbsoluteSnapshots_RejectOlderSequencesDuplicateWritesAndLostOwnership()
    {
        await using var db = await Database(); var (record, attempt) = await Start(db);
        var clock = new ExtractionDiagnosticsTests.ManualClock(); var diagnostics = new ExtractionDiagnostics(clock, NullLogger.Instance);
        using var scope = diagnostics.Enter(record.Id, record.CompanyId, attempt, 1);
        clock.Advance(25); var first = diagnostics.Snapshot(); clock.Advance(10); var latest = diagnostics.Snapshot();
        Assert.Equal(1, await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attempt, latest, default));
        Assert.Equal(0, await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attempt, first, default));
        Assert.Equal(0, await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attempt, latest, default));
        var summary = await Jobs(db).GetAsync(record.Id, record.CompanyId, default);
        Assert.Equal(35, summary!.Timings!.RecordedActiveMs);
        // An active operation advances in memory; the API must continue returning only its confirmed snapshot.
        clock.Advance(1000);
        Assert.Equal(35, (await Jobs(db).GetAsync(record.Id, record.CompanyId, default))!.Timings!.RecordedActiveMs);
        record.ActiveAttemptId = Guid.NewGuid(); await db.SaveChangesAsync();
        Assert.Equal(0, await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attempt, diagnostics.Snapshot(), default));
    }

    [PostgresFact]
    public async Task ConcurrentSnapshots_KeepHighestSequenceWithoutAccumulatingRepeatedValues()
    {
        await using var db = await Database(); var (record, attempt) = await Start(db);
        var clock = new ExtractionDiagnosticsTests.ManualClock(); var recorder = new ExtractionDiagnostics(clock, NullLogger.Instance);
        using var scope = recorder.Enter(record.Id, record.CompanyId, attempt, 1);
        var snapshots = Enumerable.Range(1, 12).Select(_ => { clock.Advance(5); return recorder.Snapshot(); }).ToArray();
        await Task.WhenAll(snapshots.Reverse().Select(async snapshot =>
        {
            await using var writer = await Database();
            await AttemptDiagnosticsStore.SaveAsync(writer, record.Id, attempt, snapshot, default);
        }));
        var saved = await db.Attempts.AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(snapshots[^1].Sequence, saved.DiagnosticsSequence);
        Assert.Equal(60, (await Jobs(db).GetAsync(record.Id, record.CompanyId, default))!.Timings!.RecordedActiveMs);
    }

    [PostgresFact]
    public async Task InterruptedRecovery_PreservesMeasuredTimeAndMarksCoverageWithoutInventingDowntime()
    {
        await using var db = await Database(); var (record, attempt) = await Start(db);
        var clock = new ExtractionDiagnosticsTests.ManualClock(); var diagnostics = new ExtractionDiagnostics(clock, NullLogger.Instance);
        using var scope = diagnostics.Enter(record.Id, record.CompanyId, attempt, 1);
        clock.Advance(50);
        await AttemptDiagnosticsStore.SaveAsync(db, record.Id, attempt, diagnostics.Snapshot(), default);
        var previous = await db.Attempts.SingleAsync(x => x.Id == attempt);
        await db.Entry(previous).ReloadAsync();
        AttemptDiagnosticsStore.MarkInterrupted(previous, DateTimeOffset.UtcNow.AddHours(2)); await db.SaveChangesAsync();
        var summary = await Jobs(db).GetAsync(record.Id, record.CompanyId, default);
        Assert.Equal(50, summary!.Timings!.RecordedActiveMs); Assert.Equal(DiagnosticsCoverage.Incomplete, summary.Timings.Coverage);
        var history = await Jobs(db).AttemptsAsync(record.Id, record.CompanyId, 0, 100, default);
        Assert.Equal("recoveryInterrupted", history!.Items[0].ErrorCode);
        Assert.Equal(DiagnosticsCoverage.Incomplete, history.Items[0].Coverage);
    }

    [PostgresFact]
    public async Task History_IsTenantScopedCursorOrderedAndLegacyMeasurementsStayUnavailable()
    {
        await using var db = await Database(); var (record, attempt) = await Start(db);
        db.Attempts.Add(new() { Id = Guid.NewGuid(), ExtractionId = record.Id, Number = 2, StartedAt = DateTimeOffset.UtcNow });
        db.Attempts.Add(new() { Id = Guid.NewGuid(), ExtractionId = record.Id, Number = 3, StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(); var jobs = Jobs(db);
        var first = await jobs.AttemptsAsync(record.Id, record.CompanyId, 0, 1, default);
        Assert.Equal("1", first!.NextCursor); Assert.Equal(attempt, first.Items[0].AttemptId);
        Assert.Null(first.Items[0].Timings); Assert.Equal(DiagnosticsCoverage.NotAvailable, first.Items[0].Coverage);
        var second = await jobs.AttemptsAsync(record.Id, record.CompanyId, 1, 500, default);
        Assert.Equal(new[] { 2, 3 }, second!.Items.Select(x => x.Number)); Assert.Null(second.NextCursor);
        Assert.Null(await jobs.AttemptsAsync(record.Id, "another-company", 0, 100, default));
        Assert.Null(await jobs.GetAsync(record.Id, "another-company", default));
        var summary = await jobs.GetAsync(record.Id, record.CompanyId, default);
        Assert.Null(summary!.Timings!.RecordedActiveMs); Assert.Null(summary.Timings.Operations);
        Assert.Equal(DiagnosticsCoverage.NotAvailable, summary.Timings.Coverage);
    }
}
