using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DescagaCompronanteSRI.Tests.Diagnostics;

public sealed class ExtractionDiagnosticsTests
{
    [Fact]
    public async Task MonotonicDurations_NestedOperationsAndRepeatedSnapshotsDoNotDoubleCount()
    {
        var clock = new ManualClock();
        var recorder = new ExtractionDiagnostics(clock, NullLogger.Instance);
        using var scope = recorder.Enter(Guid.NewGuid(), "fixture", Guid.NewGuid(), 1);
        await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Authentication, async () =>
        {
            clock.Advance(10); clock.WallShift(-TimeSpan.FromDays(2));
            await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.ProfileRead, () => { clock.Advance(20); return Task.CompletedTask; });
        });
        recorder.SetStage(ExtractionStage.Processing);
        using (var operation = ExtractionDiagnostics.StartOperation(DiagnosticOperation.Download))
        {
            clock.Advance(15);
            var pending = recorder.Snapshot();
            Assert.Equal(15, pending.Operations.Single(x => x.Name == "download").InProgressDurationMs);
            operation.Complete();
        }
        var first = recorder.Snapshot(); var second = recorder.Snapshot();
        Assert.Equal(45, first.ActiveDurationMs); Assert.Equal(first.ActiveDurationMs, second.ActiveDurationMs);
        Assert.Equal(first.Sequence + 1, second.Sequence);
        Assert.Equal(30, first.Operations.Single(x => x.Name == "authentication").TotalDurationMs);
        Assert.Equal(20, first.Operations.Single(x => x.Name == "profileRead").TotalDurationMs);
        recorder.Finish(DiagnosticsCoverage.Complete, DiagnosticOutcome.Succeeded);
        clock.Advance(500);
        Assert.Equal(45, recorder.Snapshot().ActiveDurationMs);
    }

    [Fact]
    public async Task FailuresAndCancellation_AreCountedWithoutExternalMessagesOrPayloads()
    {
        var logger = new CaptureLogger(); var clock = new ManualClock();
        var recorder = new ExtractionDiagnostics(clock, logger);
        using var scope = recorder.Enter(Guid.NewGuid(), "fixture", Guid.NewGuid(), 1);
        await Assert.ThrowsAsync<IOException>(() => ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.StorageWrite,
            () => { clock.Advance(8); return Task.FromException<bool>(new IOException("SECRET password xml connection-string authorization-code")); }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Download,
            () => Task.FromException<bool>(new OperationCanceledException("SECRET"))));
        var snapshot = recorder.Snapshot();
        Assert.Equal(1, snapshot.Operations.Single(x => x.Name == "storageWrite").FailedCount);
        Assert.Equal(1, snapshot.Operations.Single(x => x.Name == "download").CancelledCount);
        Assert.DoesNotContain("SECRET", string.Join("\n", logger.Messages));
        Assert.Contains("extractionId", logger.ScopeKeys); Assert.Contains("attemptId", logger.ScopeKeys);
        Assert.Contains("companyId", logger.ScopeKeys);
    }

    [Fact]
    public async Task LoggerFailure_DoesNotChangeBusinessResultAndContextIsIsolated()
    {
        async Task<int> Run(int index)
        {
            var recorder = new ExtractionDiagnostics(new ManualClock(), new ThrowingLogger());
            using var scope = recorder.Enter(Guid.NewGuid(), "fixture", Guid.NewGuid(), index);
            recorder.SetStage(ExtractionStage.Processing);
            var result = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Validation, async () => { await Task.Yield(); return index; });
            Assert.Single(recorder.Snapshot().Operations);
            return result;
        }
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(Run(1), Run(2)));
        Assert.Null(ExtractionDiagnostics.Current);
    }

    [Fact]
    public async Task ThousandsOfDocuments_KeepFixedSizeAggregatesAndNoDocumentHistory()
    {
        var clock = new ManualClock(); var recorder = new ExtractionDiagnostics(clock, NullLogger.Instance);
        using var scope = recorder.Enter(Guid.NewGuid(), "fixture", Guid.NewGuid(), 1);
        recorder.SetStage(ExtractionStage.Processing);
        for (var i = 0; i < 5000; i++)
            await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.StorageInspection, () => { clock.Advance(1); return Task.CompletedTask; });
        var snapshot = recorder.Snapshot();
        Assert.Single(snapshot.Operations); Assert.Equal(5000, snapshot.Operations[0].Count);
        Assert.Equal(1, snapshot.Operations[0].AverageDurationMs);
        Assert.True(ExtractionJobs.Serialize(snapshot).Length < 2000);
    }

    [Fact]
    public void FrameworkDiagnostics_CannotLeakSqlParametersOrAuthorizationUrls()
    {
        var capture = new CaptureLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddExtractionJsonConsole().AddProvider(new CaptureProvider(capture)));
        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogError("SECRET connection SQL payload");
        factory.CreateLogger("System.Net.Http.HttpClient.parser.LogicalHandler").LogError("SECRET URL?code=private");
        factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").LogError("SECRET request URL?password=private");
        Assert.Empty(capture.Messages);
        ExtractionDiagnostics.Event(factory.CreateLogger("DescagaCompronanteSRI.Jobs.ExtractionWorker"), LogLevel.Information,
            DiagnosticEvent.AttemptStarted, code: "controlled");
        Assert.Single(capture.Messages);
    }
    private sealed class CaptureProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string category) => logger;
        public void Dispose() { }
    }

    internal sealed class ManualClock : TimeProvider
    {
        private long ticks; private DateTimeOffset utc = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => utc;
        public void Advance(double ms) { var delta = TimeSpan.FromMilliseconds(ms); ticks += delta.Ticks; utc += delta; }
        public void WallShift(TimeSpan shift) => utc += shift;
    }
    internal sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public HashSet<string> ScopeKeys { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        { if (state is IEnumerable<KeyValuePair<string, object>> values) foreach (var v in values) ScopeKeys.Add(v.Key); return null; }
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) => Messages.Add(format(state, error));
    }
    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => throw new IOException("sink offline");
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) => throw new IOException("sink offline");
    }
}
