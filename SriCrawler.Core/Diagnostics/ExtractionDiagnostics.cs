using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Serialization;

namespace DescagaCompronanteSRI.Diagnostics;

[JsonConverter(typeof(ApiEnumConverter<DiagnosticsCoverage>))]
public enum DiagnosticsCoverage { NotAvailable, Recording, Complete, Incomplete }
[JsonConverter(typeof(ApiEnumConverter<DiagnosticOperation>))]
public enum DiagnosticOperation
{
    BrowserStartup, SessionCleanup, Authentication, ProfileRead, PortalAccess, Query,
    TableRead, Pagination, CandidateLookup, ReuseLookup, StorageInspection, StorageRead,
    Download, Validation, StorageWrite, LegacyParsing, JsonParsing, Persistence
}
[JsonConverter(typeof(ApiEnumConverter<DiagnosticOutcome>))]
public enum DiagnosticOutcome { Succeeded, Failed, Cancelled, Unsupported }
public enum DiagnosticEvent
{
    Accepted = 1300, IdempotentMatch, Dispatched, AttemptStarted, TaxpayerBusy, StageChanged,
    PageProcessed, RetryScheduled, AttemptInterrupted, AttemptFinished, OperationFinished,
    ProfileUnavailable, BrowserEvent, TransportRetry, ParserRequest, DispatchRecovered, DiagnosticsUnavailable, DocumentIssue
}

public sealed record TimingAggregate(string Name, long Count, double TotalDurationMs,
    double AverageDurationMs, double MaxDurationMs, long SucceededCount, long FailedCount,
    long CancelledCount, long UnsupportedCount, double InProgressDurationMs = 0);
public sealed record AttemptDiagnosticsSnapshot(int Version, long Sequence, DiagnosticsCoverage Coverage,
    DateTimeOffset CapturedAt, double ActiveDurationMs, IReadOnlyList<TimingAggregate> Stages,
    IReadOnlyList<TimingAggregate> Operations);
public sealed record ExtractionTimings(double ElapsedMs, double InitialQueueMs, double? RecordedActiveMs,
    DiagnosticsCoverage Coverage, DateTimeOffset? UpdatedAt, IReadOnlyList<TimingAggregate>? Stages,
    IReadOnlyList<TimingAggregate>? Operations);
public sealed record ExtractionAttemptSummary(Guid AttemptId, int Number, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, string? ErrorCode, AttemptDiagnosticsSnapshot? Timings,
    DiagnosticsCoverage Coverage);

/// <summary>A bounded recorder shared by the asynchronous flows of one attempt only.</summary>
public sealed class ExtractionDiagnostics(TimeProvider timeProvider, ILogger logger)
{
    private readonly TimeProvider clock = timeProvider;
    private static readonly AsyncLocal<ExtractionDiagnostics?> Ambient = new();
    public static ExtractionDiagnostics? Current => Ambient.Value;
    private readonly object gate = new();
    private readonly Dictionary<DiagnosticOperation, Aggregate> operations = [];
    private readonly Dictionary<ExtractionStage, Aggregate> stages = [];
    private readonly Dictionary<DiagnosticOperation, List<long>> runningOperations = [];
    private long started, stageStarted, sequence;
    private double finishedDuration;
    private ExtractionStage stage = ExtractionStage.Login;
    private DiagnosticsCoverage coverage = DiagnosticsCoverage.Recording;
    private bool finished;

    public IDisposable Enter(Guid extractionId, string companyId, Guid attemptId, int attemptNumber)
    {
        started = stageStarted = clock.GetTimestamp();
        var previous = Ambient.Value;
        Ambient.Value = this;
        IDisposable? scope = null;
        try { scope = logger.BeginScope(new Dictionary<string, object> { ["extractionId"] = extractionId,
            ["companyId"] = companyId, ["attemptId"] = attemptId, ["attempt"] = attemptNumber }); } catch { }
        return new ActionScope(() => { try { scope?.Dispose(); } catch { } Ambient.Value = previous; });
    }

    public void SetStage(ExtractionStage next)
    {
        ExtractionStage previous; double duration;
        lock (gate)
        {
            if (finished || next == stage) return;
            previous = stage; duration = Elapsed(stageStarted);
            Add(stages, stage, duration, DiagnosticOutcome.Succeeded);
            stage = next; stageStarted = clock.GetTimestamp();
        }
        Event(logger, LogLevel.Information, DiagnosticEvent.StageChanged, stage: previous,
            outcome: DiagnosticOutcome.Succeeded, durationMs: duration, detail: Name(next));
    }

    public void Finish(DiagnosticsCoverage finalCoverage, DiagnosticOutcome outcome)
    {
        lock (gate)
        {
            if (finished) { if (finalCoverage == DiagnosticsCoverage.Incomplete) coverage = finalCoverage; return; }
            Add(stages, stage, Elapsed(stageStarted), outcome);
            finishedDuration = Elapsed(started);
            finished = true; coverage = finalCoverage;
        }
        Event(logger, LogLevel.Information, DiagnosticEvent.StageChanged, stage: stage, outcome: outcome,
            durationMs: stages[stage].Export(Name(stage)).TotalDurationMs, detail: "finished");
    }

    public AttemptDiagnosticsSnapshot Snapshot()
    {
        lock (gate)
        {
            var stageValues = stages.ToDictionary(x => x.Key, x => x.Value.Export(Name(x.Key)));
            if (!finished)
            {
                stageValues.TryGetValue(stage, out var value);
                stageValues[stage] = (value ?? new Aggregate().Export(Name(stage))) with { InProgressDurationMs = Elapsed(stageStarted) };
            }
            var operationValues = operations.ToDictionary(x => x.Key, x => x.Value.Export(Name(x.Key)));
            foreach (var running in runningOperations)
            {
                operationValues.TryGetValue(running.Key, out var value);
                operationValues[running.Key] = (value ?? new Aggregate().Export(Name(running.Key))) with
                { InProgressDurationMs = running.Value.Sum(t => Elapsed(t)) };
            }
            return new(1, ++sequence, coverage, clock.GetUtcNow(), finished ? finishedDuration : Elapsed(started),
                stageValues.OrderBy(x => x.Key).Select(x => x.Value).ToArray(),
                operationValues.OrderBy(x => x.Key).Select(x => x.Value).ToArray());
        }
    }

    public static async Task<T> MeasureAsync<T>(DiagnosticOperation operation, Func<Task<T>> action,
        Func<T, DiagnosticOutcome>? classify = null)
    {
        var recorder = Current;
        if (recorder is null) return await action();
        var timestamp = recorder.Start(operation);
        var outcome = DiagnosticOutcome.Failed;
        try { var value = await action(); outcome = classify?.Invoke(value) ?? DiagnosticOutcome.Succeeded; return value; }
        catch (OperationCanceledException) { outcome = DiagnosticOutcome.Cancelled; throw; }
        finally { recorder.Record(operation, timestamp, outcome); }
    }
    public static Task MeasureAsync(DiagnosticOperation operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });
    private long Start(DiagnosticOperation operation)
    {
        var timestamp = clock.GetTimestamp();
        lock (gate) { if (!runningOperations.TryGetValue(operation, out var active)) runningOperations[operation] = active = []; active.Add(timestamp); }
        return timestamp;
    }
    private void Record(DiagnosticOperation operation, long timestamp, DiagnosticOutcome outcome)
    {
        var duration = Elapsed(timestamp);
        lock (gate)
        {
            if (runningOperations.TryGetValue(operation, out var active)) { active.Remove(timestamp); if (active.Count == 0) runningOperations.Remove(operation); }
            if (!finished) Add(operations, operation, duration, outcome);
        }
        Event(logger, outcome == DiagnosticOutcome.Succeeded ? LogLevel.Debug : LogLevel.Warning,
            DiagnosticEvent.OperationFinished, stage: stage, operation: operation, outcome: outcome, durationMs: duration);
    }
    private double Elapsed(long timestamp) => Math.Max(0, clock.GetElapsedTime(timestamp).TotalMilliseconds);
    public static string Name<T>(T value) where T : struct, Enum => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    private static void Add<T>(Dictionary<T, Aggregate> values, T key, double duration, DiagnosticOutcome outcome) where T : notnull
    { if (!values.TryGetValue(key, out var aggregate)) values[key] = aggregate = new(); aggregate.Add(duration, outcome); }
    private sealed class Aggregate
    {
        private long count, succeeded, failed, cancelled, unsupported;
        private double total, max;
        public void Add(double duration, DiagnosticOutcome outcome)
        {
            count++; total += duration; max = Math.Max(max, duration);
            switch (outcome) { case DiagnosticOutcome.Succeeded: succeeded++; break; case DiagnosticOutcome.Failed: failed++; break;
                case DiagnosticOutcome.Cancelled: cancelled++; break; case DiagnosticOutcome.Unsupported: unsupported++; break; }
        }
        public TimingAggregate Export(string name) => new(name, count, total, count == 0 ? 0 : total / count, max, succeeded, failed, cancelled, unsupported);
    }
    public static OperationMeasurement StartOperation(DiagnosticOperation operation, CancellationToken token = default) => new(Current, operation, token);
    public sealed class OperationMeasurement : IDisposable
    {
        private readonly ExtractionDiagnostics? recorder;
        private readonly DiagnosticOperation operation;
        private readonly CancellationToken token;
        private readonly long timestamp;
        private DiagnosticOutcome outcome = DiagnosticOutcome.Failed;
        private bool disposed;
        internal OperationMeasurement(ExtractionDiagnostics? recorder, DiagnosticOperation operation, CancellationToken token)
        { this.recorder = recorder; this.operation = operation; this.token = token; timestamp = recorder?.Start(operation) ?? 0; }
        public void Complete(DiagnosticOutcome result = DiagnosticOutcome.Succeeded) => outcome = result;
        public void Dispose() { if (disposed) return; disposed = true; recorder?.Record(operation, timestamp, token.IsCancellationRequested ? DiagnosticOutcome.Cancelled : outcome); }
    }
    public static void Safely(Action action) { try { action(); } catch { } }
    public static IDisposable SafeScope(ILogger logger, Dictionary<string, object> values)
    {
        try { var scope = logger.BeginScope(values); return new ActionScope(() => Safely(() => scope?.Dispose())); }
        catch { return new ActionScope(() => { }); }
    }
    public static IDisposable DocumentScope(ILogger logger, int page, int row, string format) =>
        SafeScope(logger, new Dictionary<string, object> { ["pageNumber"] = page, ["rowIndex"] = row,
            ["format"] = format, ["stage"] = Current is null ? "processing" : Name(Current.stage) });
    public static void Event(ILogger logger, LogLevel level, DiagnosticEvent id, string? code = null,
        string? errorType = null, ExtractionStage? stage = null, DiagnosticOperation? operation = null,
        DiagnosticOutcome? outcome = null, double? durationMs = null, object? detail = null)
    {
        stage ??= Current?.stage;
        // Only controlled values belong here. Never pass external exception messages or payloads.
        try { logger.Log(level, new EventId((int)id, id.ToString()),
            "Event {event} stage {stage} operation {operation} outcome {outcome} duration {durationMs} code {errorCode} type {errorType} detail {detail}",
            Name(id), stage is null ? null : Name(stage.Value), operation is null ? null : Name(operation.Value),
            outcome is null ? null : Name(outcome.Value), durationMs, code, errorType, detail); } catch { }
    }
    private sealed class ActionScope(Action action) : IDisposable
    { private bool disposed; public void Dispose() { if (disposed) return; disposed = true; action(); } }
}

public static class DiagnosticLogging
{
    public static ILoggingBuilder AddExtractionJsonConsole(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(o => { o.IncludeScopes = true; o.UseUtcTimestamp = true; o.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"; });
        // Framework database/HTTP diagnostics can include command parameters or remote URLs.
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        logging.AddFilter("Npgsql", LogLevel.None);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
        return logging;
    }
}
