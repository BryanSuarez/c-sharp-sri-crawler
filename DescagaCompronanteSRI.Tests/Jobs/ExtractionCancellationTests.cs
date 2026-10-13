using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Tests.Security;
using DescagaCompronanteSRI.Validation;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DescagaCompronanteSRI.Tests.Storage;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Fixture = DescagaCompronanteSRI.Tests.Jobs.CompanyWorkloadTests.Fixture;

namespace DescagaCompronanteSRI.Tests.Jobs;

[Collection("HangfireHost")]
public sealed class ExtractionCancellationTests
{
    private static ReceivedDocumentsQuery Query(string company = "cancel-a") => new()
    { CompanyId = company, User = "1790012345001", Password = "cancellation-fixture-only", Year = 2026, Month = 5,
        DocumentType = DocumentType.Invoice, DownloadFormat = DownloadFormat.Pdf };

    private static ExtractionJobs Jobs(Fixture f, CrawlerDbContext db) => new(db,
        f.Services.GetRequiredService<CredentialCipher>(), Options.Create(f.Options));
    private static async Task<ExtractionCancellation?> Cancel(Fixture f, Guid id, string company = "cancel-a", Guid? actor = null)
    { await using var db = f.Db(); return await Jobs(f, db).CancelAsync(id, company, actor, default); }
    private static async Task<ExtractionSummary> Status(Fixture f, Guid id)
    { await using var db = f.Db(); return (await Jobs(f, db).GetAsync(id, "cancel-a", default))!; }
    private static PersistentExtractionProgress Progress(Fixture f, Guid id, Guid attempt) => new(
        f.Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), f.Services.GetRequiredService<ISriDocumentJsonParser>(),
        f.Services.GetRequiredService<IDocumentStorage>(), id, attempt, Query(), f.Services.GetRequiredService<IDocumentValidator>(),
        f.Services.GetRequiredService<IOptions<DocumentValidationOptions>>());

    [Theory]
    [InlineData(0)] [InlineData(31)]
    public void PollingInterval_IsBounded(int seconds)
    {
        var settings = new ExtractionJobOptions { CancellationPollIntervalSeconds = seconds };
        Assert.False(Validator.TryValidateObject(settings, new ValidationContext(settings), [], true));
    }

    [PostgresFact]
    public async Task QueuedAndRetryingCancellation_IsIdempotentAndNeverStartsAnAttempt()
    {
        await using var f = await Fixture.CreateAsync();
        foreach (var state in new[] { JobStatus.Queued, JobStatus.Retrying })
        {
            var accepted = await f.Accept(Query());
            await using (var db = f.Db()) await db.Extractions.Where(x => x.Id == accepted.ExtractionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, state));
            var actor = Guid.NewGuid();
            var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Cancel(f, accepted.ExtractionId, actor: actor)));
            Assert.All(replies, x => { Assert.Equal(JobStatus.Cancelled, x!.JobStatus); Assert.NotNull(x.CancelledAt); });
            Assert.Single(replies.Select(x => x!.CancellationRequestedAt).Distinct());
            var runner = new CancellationRunner();
            await f.Worker(runner).ExecuteAsync(accepted.ExtractionId, default);
            await f.Dispatch();
            Assert.False(runner.Started.Task.IsCompleted);
            await using var final = f.Db();
            var record = await final.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
            Assert.Null(record.EncryptedPassword); Assert.Equal(actor, record.CancellationRequestedBy);
            Assert.Equal(0, record.AttemptCount);
            Assert.Single(await final.Failures.Where(x => x.ExtractionId == record.Id).ToListAsync());
            var result = await Jobs(f, final).ResultAsync(record.Id, record.CompanyId, default);
            Assert.Equal(JobStatus.Cancelled, result!.JobStatus); Assert.Equal(ExtractionStatus.Failed, result.Status);
            Assert.Contains(result.Errors, x => x.Code == ExtractionErrorCode.ExtractionCancelled);
        }
        Assert.Empty(f.Queue.Ids);
    }

    [PostgresFact]
    public async Task CancellationDuringRetryBackoff_PreservesTheFinishedAttemptHistory()
    {
        await using var f = await Fixture.CreateAsync(); var accepted = await f.Accept(Query());
        var finished = DateTimeOffset.UtcNow.AddMinutes(-1); finished = ExtractionLifecycle.Timestamp(finished);
        var attempt = Guid.NewGuid();
        await using (var db = f.Db())
        {
            var record = await db.Extractions.SingleAsync(); record.JobStatus = JobStatus.Retrying;
            record.ActiveAttemptId = attempt; record.AttemptCount = 1;
            db.Attempts.Add(new() { Id = attempt, ExtractionId = record.Id, Number = 1, StartedAt = finished.AddSeconds(-5),
                FinishedAt = finished, ErrorCode = "interrupted" }); await db.SaveChangesAsync();
        }
        await Cancel(f, accepted.ExtractionId);
        await using var final = f.Db(); var history = await final.Attempts.SingleAsync();
        Assert.Equal(finished, history.FinishedAt); Assert.Equal("interrupted", history.ErrorCode);
        Assert.Equal(JobStatus.Cancelled, (await Status(f, accepted.ExtractionId)).JobStatus);
    }

    [PostgresFact]
    public async Task CancellationFreesPendingCapacityButDoesNotRefundAcceptedUsageOrIdempotency()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxPendingPerCompany = 1; f.Options.MaxAcceptedPerCompany24Hours = 2;
        var request = Guid.NewGuid(); var first = await f.Accept(Query(), request);
        await Cancel(f, first.ExtractionId);
        var repeat = await f.Accept(Query(), request);
        Assert.Equal(first.ExtractionId, repeat.ExtractionId); Assert.Equal(JobStatus.Cancelled, repeat.JobStatus);
        var second = await f.Accept(Query()); await Cancel(f, second.ExtractionId);
        var limit = await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query()));
        Assert.Equal(WorkloadLimitCode.CompanyUsageLimitReached, limit.Result.Code);
        Assert.Equal(2, limit.Result.Current);
    }

    [PostgresFact]
    public async Task RunningCancellation_PreservesResultsAndWaitsForCleanupBeforeReleasingCapacity()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.CancellationPollIntervalSeconds = 1; f.Options.MaxPendingPerCompany = 1;
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        var accepted = await f.Accept(Query()); var runner = new CancellationRunner(saveDocument: true, storage);
        var executing = f.Worker(runner).ExecuteAsync(accepted.ExtractionId, default);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var requested = await Cancel(f, accepted.ExtractionId);
            Assert.Equal(JobStatus.CancellationRequested, requested!.JobStatus); Assert.Null(requested.CancelledAt);
            await runner.Closing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(executing.IsCompleted);
            // Even expired credentials must not release a still-closing worker's reservation.
            await using (var db = f.Db()) await db.Extractions.Where(x => x.Id == accepted.ExtractionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));
            await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query()));
            await f.Dispatch();
            Assert.Equal(JobStatus.CancellationRequested, (await Status(f, accepted.ExtractionId)).JobStatus);
            Assert.Equal(requested.CancellationRequestedAt, (await Cancel(f, accepted.ExtractionId))!.CancellationRequestedAt);
            var attempt = (await Status(f, accepted.ExtractionId)).Attempts;
            Assert.Equal(1, attempt);
            await using var active = f.Db();
            var record = await active.Extractions.SingleAsync(); Assert.Null(record.EncryptedPassword);
            var progress = Progress(f, record.Id, record.ActiveAttemptId!.Value);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => progress.StageAsync(ExtractionStage.Query, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => progress.CheckpointAsync(new() { CompanyId = "cancel-a", TaxpayerId = Query().User }, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => progress.SaveAsync(new() { PageNumber = 2, RowIndex = 0, DownloadFormat = DownloadFormat.Pdf, DownloadStatus = DocumentDownloadStatus.Failed }, default));
        }
        finally { runner.ReleaseCleanup.TrySetResult(); await executing.WaitAsync(TimeSpan.FromSeconds(10)); }
        var summary = await Status(f, accepted.ExtractionId);
        Assert.Equal(JobStatus.Cancelled, summary.JobStatus); Assert.Equal(ExtractionStatus.Partial, summary.ExtractionStatus);
        Assert.Equal(1, summary.DownloadedCount); Assert.Equal(1, summary.DiscoveredCount); Assert.Equal(0, summary.FailedCount);
        Assert.Equal(PaginationStatus.Incomplete, summary.Pagination.Status);
        Assert.Equal(DiagnosticsCoverage.Incomplete, summary.Timings!.Coverage);
        await using var final = f.Db();
        Assert.Single(await final.Files.ToListAsync()); Assert.Single(await final.Results.ToListAsync());
        await using (var stored = await storage.OpenReadAsync(runner.Stored!))
        {
            using var bytes = new MemoryStream(); await stored.CopyToAsync(bytes);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", "image-only.pdf")), bytes.ToArray());
        }
        Assert.Equal("extractionCancelled", (await final.Attempts.SingleAsync()).ErrorCode);
        Assert.NotNull((await final.Attempts.SingleAsync()).FinishedAt);
        await f.Worker(new CancellationRunner()).ExecuteAsync(accepted.ExtractionId, default);
        Assert.Empty(f.Queue.Ids); await f.Accept(Query());
    }

    [PostgresFact]
    public async Task AbandonedCancellation_IsRecoveredWithoutOpeningAnotherSession()
    {
        await using var f = await Fixture.CreateAsync(); var accepted = await f.Accept(Query());
        var attempt = Guid.NewGuid(); await using (var db = f.Db())
        {
            var e = await db.Extractions.SingleAsync(); e.JobStatus = JobStatus.Running; e.AttemptCount = 1; e.ActiveAttemptId = attempt;
            e.StartedAt = DateTimeOffset.UtcNow;
            db.Attempts.Add(new() { Id = attempt, ExtractionId = e.Id, Number = 1, StartedAt = e.StartedAt.Value }); await db.SaveChangesAsync();
        }
        await using var connection = new NpgsqlConnection(f.Connection + ";Pooling=false"); await connection.OpenAsync();
        Assert.True(await ExtractionLifecycle.TryLockTaxpayerAsync(connection, Query().User, default));
        await Cancel(f, accepted.ExtractionId);
        await f.Dispatch(); Assert.Equal(JobStatus.CancellationRequested, (await Status(f, accepted.ExtractionId)).JobStatus);
        await connection.CloseAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await Status(f, accepted.ExtractionId)).JobStatus != JobStatus.Cancelled)
        { await f.Dispatch(); await Task.Delay(25, deadline.Token); }
        await f.Dispatch();
        await using var final = f.Db(); Assert.Single(await final.Attempts.ToListAsync()); Assert.Single(await final.Failures.ToListAsync());
        Assert.Empty(f.Queue.Ids);
    }

    [PostgresFact]
    public async Task CancellationAndStartRace_NeverResurrectsOrRetriesCancelledWork()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.CancellationPollIntervalSeconds = 1;
        for (var i = 0; i < 8; i++)
        {
            var accepted = await f.Accept(Query()); var runner = new CancellationRunner(); runner.ReleaseCleanup.TrySetResult();
            var executing = f.Worker(runner).ExecuteAsync(accepted.ExtractionId, default);
            await Cancel(f, accepted.ExtractionId);
            await executing.WaitAsync(TimeSpan.FromSeconds(10));
            await f.Dispatch(); await f.Worker(runner).ExecuteAsync(accepted.ExtractionId, default);
            Assert.Equal(JobStatus.Cancelled, (await Status(f, accepted.ExtractionId)).JobStatus);
        }
        Assert.Empty(f.Queue.Ids);
    }

    [PostgresFact]
    public async Task CancellationWinsOverShutdownAndCompletionAfterTheRequest()
    {
        await using var f = await Fixture.CreateAsync();
        var accepted = await f.Accept(Query()); var runner = new CancellationRunner();
        using var shutdown = new CancellationTokenSource(); var executing = f.Worker(runner).ExecuteAsync(accepted.ExtractionId, shutdown.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Cancel(f, accepted.ExtractionId);
        shutdown.Cancel(); runner.ReleaseCleanup.TrySetResult(); await executing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(JobStatus.Cancelled, (await Status(f, accepted.ExtractionId)).JobStatus); Assert.Empty(f.Queue.Ids);
        var second = await f.Accept(Query()); var completing = new FinishingRunner();
        var finish = f.Worker(completing).ExecuteAsync(second.ExtractionId, default);
        await completing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Cancel(f, second.ExtractionId);
        completing.Release.TrySetResult(); await finish.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(JobStatus.Cancelled, (await Status(f, second.ExtractionId)).JobStatus);
    }

    [PostgresFact]
    public async Task CompletionAndCancellationRace_ProducesOneTerminalOutcome()
    {
        await using var f = await Fixture.CreateAsync();
        for (var i = 0; i < 10; i++)
        {
            var accepted = await f.Accept(Query()); var runner = new FinishingRunner();
            var executing = f.Worker(runner).ExecuteAsync(accepted.ExtractionId, default);
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            runner.Release.TrySetResult();
            var cancelled = true;
            try { await Cancel(f, accepted.ExtractionId); }
            catch (ExtractionAlreadyFinishedException) { cancelled = false; }
            await executing.WaitAsync(TimeSpan.FromSeconds(10));
            var summary = await Status(f, accepted.ExtractionId);
            Assert.Equal(cancelled ? JobStatus.Cancelled : JobStatus.Completed, summary.JobStatus);
            await using var db = f.Db();
            Assert.Equal(cancelled ? 1 : 0, await db.Failures.CountAsync(x => x.ExtractionId == accepted.ExtractionId));
            Assert.Equal(1, summary.Attempts);
        }
        Assert.Empty(f.Queue.Ids);
    }

    [PostgresFact]
    public async Task FinishedAndForeignExtractions_CannotBeCancelled()
    {
        await using var f = await Fixture.CreateAsync(); var accepted = await f.Accept(Query());
        Assert.Null(await Cancel(f, accepted.ExtractionId, "cancel-b")); Assert.Null(await Cancel(f, Guid.NewGuid()));
        foreach (var terminal in new[] { JobStatus.Completed, JobStatus.Failed })
        {
            await f.Finish(accepted.ExtractionId, terminal);
            await Assert.ThrowsAsync<ExtractionAlreadyFinishedException>(() => Cancel(f, accepted.ExtractionId));
            Assert.Equal(terminal, (await Status(f, accepted.ExtractionId)).JobStatus);
        }
    }

    [PostgresFact]
    public async Task RealHangfireCancellation_StopsActiveDeliveryWithoutAnotherAttempt()
    {
        await using var f = await Fixture.CreateAsync(realHangfire: true); f.Options.CancellationPollIntervalSeconds = 1;
        var accepted = await f.Accept(Query());
        var storage = f.Services.GetRequiredService<JobStorage>();
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1,
            Activator = new CompanyWorkloadTests.ScopedActivator(f.Services.GetRequiredService<IServiceScopeFactory>()), ShutdownTimeout = TimeSpan.FromSeconds(10) }, storage);
        await f.Dispatch(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while ((await Status(f, accepted.ExtractionId)).JobStatus != JobStatus.Running) await Task.Delay(50, deadline.Token);
        await Cancel(f, accepted.ExtractionId);
        while ((await Status(f, accepted.ExtractionId)).JobStatus != JobStatus.Cancelled) await Task.Delay(50, deadline.Token);
        await using var db = f.Db(); Assert.Single(await db.Attempts.ToListAsync());
        var delivery = await db.Dispatches.Select(x => x.HangfireJobId).SingleAsync();
        while (true)
        {
            using var connection = storage.GetConnection(); if (connection.GetJobData(delivery!).State == "Succeeded") break;
            await Task.Delay(50, deadline.Token);
        }
        Assert.Equal(1, (await Status(f, accepted.ExtractionId)).Attempts);
    }

    [PostgresFact]
    public async Task ProtectedHttpCancellation_EnforcesCompanyAndSupportsSyncResult()
    {
        await using var f = await Fixture.CreateAsync();
        await using var original = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        await using var factory = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExtractionJobs:ConnectionString"] = f.Connection, ["ExtractionJobs:SyncWaitSeconds"] = "5" })));
        using var admin = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") }); admin.DefaultRequestHeaders.Add("XApiKey", original.Admin.Token);
        using var anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var a = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var b = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        foreach (var pair in new[] { (Client: a, Company: "cancel-a"), (Client: b, Company: "cancel-b") })
        {
            var created = await admin.PostAsJsonAsync($"/api/admin/companies/{pair.Company}/api-keys", new { name = "Cancellation fixture" });
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            pair.Client.DefaultRequestHeaders.Add("XApiKey", json.RootElement.GetProperty("apiKey").GetString());
        }
        var idempotency = Guid.NewGuid();
        object Body(string mode) => new { companyId = "cancel-a", user = Query().User, password = "fixture-only", year = 2026,
            month = 5, day = 0, documentType = "invoice", downloadFormat = "pdf", executionMode = mode, clientRequestId = idempotency };
        var reply = await a.PostAsJsonAsync("/api/received-documents/query", Body("async"));
        var accepted = (await reply.Content.ReadFromJsonAsync<ExtractionAccepted>())!;
        var path = $"/api/received-documents/extractions/{accepted.ExtractionId}/cancel?companyId=cancel-a";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync(path.Replace("cancel-a", "cancel-b"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync(path, null)).StatusCode);
        var sync = await a.PostAsJsonAsync("/api/received-documents/query", Body("sync"));
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        using var result = JsonDocument.Parse(await sync.Content.ReadAsStringAsync());
        Assert.Equal("cancelled", result.RootElement.GetProperty("jobStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, result.RootElement.GetProperty("cancelledAt").ValueKind);
        await using var db = f.Db(); Assert.NotNull((await db.Extractions.SingleAsync()).CancellationRequestedBy);
        var runningJob = await f.Accept(Query()); var runner = new CancellationRunner();
        var executing = f.Worker(runner).ExecuteAsync(runningJob.ExtractionId, default);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var pending = await a.PostAsync(path.Replace(accepted.ExtractionId.ToString(), runningJob.ExtractionId.ToString()), null);
            Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
            var body = (await pending.Content.ReadFromJsonAsync<ExtractionCancellation>())!;
            Assert.Equal(JobStatus.CancellationRequested, body.JobStatus); Assert.Null(body.CancelledAt);
        }
        finally { runner.ReleaseCleanup.TrySetResult(); await executing.WaitAsync(TimeSpan.FromSeconds(10)); }
        var finished = await f.Accept(Query()); await f.Finish(finished.ExtractionId);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync(path.Replace(accepted.ExtractionId.ToString(), finished.ExtractionId.ToString()), null)).StatusCode);
        var logs = string.Join('\n', original.Logs.Messages);
        Assert.DoesNotContain("fixture-only", logs); Assert.DoesNotContain(original.Admin.Token, logs);
        Assert.DoesNotContain(a.DefaultRequestHeaders.GetValues("XApiKey").Single(), logs);
    }

    [Fact]
    public async Task CancellationPersistenceFailure_ReturnsControlled503AndDoesNotLeakSecrets()
    {
        await using var original = new ApiSecurityTests.SecurityFactory();
        await using var factory = original.WithWebHostBuilder(b => b.ConfigureServices(services =>
        { services.RemoveAll<IExtractionJobs>(); services.AddScoped<IExtractionJobs, ExtractionJobs>(); }));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("XApiKey", original.Company.Token);
        var response = await client.PostAsync($"/api/received-documents/extractions/{Guid.NewGuid()}/cancel?companyId=acme", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("persistenceUnavailable", json.RootElement.GetProperty("code").GetString());
        var logs = string.Join('\n', original.Logs.Messages);
        Assert.DoesNotContain(original.Company.Token, logs); Assert.DoesNotContain(Convert.ToBase64String(original.Company.Hash), logs);
        Assert.DoesNotContain("Host=127.0.0.1", logs);
    }

    [PostgresFact]
    public async Task AdditiveMigration_PreservesHistoricalRecordsWithNoCancellationEvidence()
    {
        await using var f = await Fixture.CreateAsync(); var accepted = await f.Accept(Query());
        await using var db = f.Db();
        await db.GetService<IMigrator>().MigrateAsync("20261013025150_CompanyWorkloadIndexes");
        await db.Database.MigrateAsync();
        var record = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
        Assert.Null(record.CancellationRequestedAt); Assert.Null(record.CancelledAt); Assert.Null(record.CancellationRequestedBy);
        Assert.Equal(JobStatus.Queued, record.JobStatus); Assert.NotNull(record.EncryptedPassword);
        Assert.Single(await db.Dispatches.ToListAsync());
    }

    private sealed class CancellationRunner(bool saveDocument = false, IDocumentStorage? storage = null) : IReceivedExtractionRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DocumentStorageReference? Stored { get; private set; }
        public TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            try
            {
                if (saveDocument)
                {
                    var response = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true };
                    response.Pagination.PagesProcessed = 1;
                    await progress.CheckpointAsync(response, token);
                    await using var content = new DocumentContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", "image-only.pdf")), DownloadFormat.Pdf);
                    var context = new DocumentStorageContext(query.CompanyId, query.User, query.Year, query.Month, DocumentDirection.Received, query.DocumentType, new string('1', 49));
                    var validation = await new PdfDocumentValidator(Options.Create(new DocumentValidationOptions())).ValidateAsync(content, query.DocumentType, new string('1', 49), token);
                    Assert.Equal(DocumentValidationStatus.Valid, validation.Status);
                    Stored = await storage!.SaveAsync(context, content, token);
                    await progress.SaveAsync(new() { PageNumber = 1, RowIndex = 0, Metadata = new() { AuthorizationNumber = new string('1', 49) },
                        DownloadFormat = DownloadFormat.Pdf, DownloadStatus = DocumentDownloadStatus.Downloaded, StorageStatus = DocumentStorageStatus.Stored,
                        ParseStatus = DocumentParseStatus.NotApplicable, Storage = Stored, Validation = validation }, token);
                }
                Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Unreachable.");
            }
            finally { Closing.TrySetResult(); await ReleaseCleanup.Task; }
        }
    }
    private sealed class FinishingRunner : IReceivedExtractionRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            Started.TrySetResult(); await Release.Task;
            var response = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true };
            response.Pagination.Status = PaginationStatus.Completed; return response;
        }
    }
}
