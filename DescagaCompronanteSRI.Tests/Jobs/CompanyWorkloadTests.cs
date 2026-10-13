using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
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
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DescagaCompronanteSRI.Tests.Jobs;

[Collection("HangfireHost")]
public sealed class CompanyWorkloadTests
{
    private static ReceivedDocumentsQuery Query(string company = "workload-a", string taxpayer = "1790012345001") => new()
    { CompanyId = company, User = taxpayer, Password = "workload-fixture-only", Year = 2026, Month = 5,
        DocumentType = DocumentType.Invoice, DownloadFormat = DownloadFormat.Xml, DownloadPolicy = DownloadPolicy.ReuseValid };

    [Theory]
    [InlineData(nameof(ExtractionJobOptions.MaxPendingPerCompany), 0)]
    [InlineData(nameof(ExtractionJobOptions.MaxAcceptedPerCompany24Hours), -1)]
    [InlineData(nameof(ExtractionJobOptions.MaxConcurrentPerCompany), 65)]
    [InlineData(nameof(ExtractionJobOptions.MaxDispatchedExtractions), 0)]
    public void WorkloadConfiguration_RejectsInvalidBounds(string field, int value)
    {
        var options = new ExtractionJobOptions();
        typeof(ExtractionJobOptions).GetProperty(field)!.SetValue(options, value);
        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), [], true));
    }

    [PostgresFact]
    public async Task ConcurrentAdmission_EnforcesPendingLimitAndDoesNotPersistRejectedWork()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxPendingPerCompany = 3;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try { await f.Accept(Query()); return true; }
            catch (WorkloadLimitExceededException e) { Assert.Equal(WorkloadLimitCode.CompanyPendingLimitReached, e.Result.Code); return false; }
        }));
        Assert.Equal(3, results.Count(x => x));
        await using var db = f.Db();
        Assert.Equal(3, await db.Extractions.CountAsync()); Assert.Equal(3, await db.Dispatches.CountAsync());
        await f.Accept(Query("workload-b"));
        Assert.Equal(4, await db.Extractions.CountAsync());
    }

    [PostgresFact]
    public async Task ConcurrentAdmission_EnforcesRollingQuotaEvenWhenWorkFinishesImmediately()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxAcceptedPerCompany24Hours = 3;
        var accepted = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try { var job = await f.Accept(Query()); await f.Finish(job.ExtractionId); return true; }
            catch (WorkloadLimitExceededException e)
            { Assert.Equal(WorkloadLimitCode.CompanyUsageLimitReached, e.Result.Code); return false; }
        }));
        Assert.Equal(3, accepted.Count(x => x));
        await using var db = f.Db(); Assert.Equal(3, await db.Extractions.CountAsync());
    }

    [PostgresFact]
    public async Task LargeExistingBacklog_RemainsPersistentAndDispatchesOnlyAvailableCompanySlots()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxDispatchedExtractions = 4;
        await using var db = f.Db(); var now = DateTimeOffset.UtcNow;
        // Simulate an existing backlog admitted before limits were enabled, with no document bodies.
        for (var i = 0; i < 1000; i++)
        {
            var id = Guid.NewGuid();
            db.Extractions.Add(new() { Id = id, CompanyId = "backlog-" + i % 10, TaxpayerId = "1790012345001",
                CreatedAt = now.AddMinutes(-10).AddMilliseconds(i), ExpiresAt = now.AddHours(1), EncryptedPassword = "synthetic-unused" });
            db.Dispatches.Add(new() { Id = Guid.NewGuid(), ExtractionId = id });
        }
        await db.SaveChangesAsync();
        await f.Dispatch(); await f.Dispatch();
        Assert.Equal(4, f.Queue.Ids.Count);
        Assert.Equal(996, await db.Dispatches.CountAsync(x => x.DispatchedAt == null));
        Assert.Equal(4, await db.Extractions.Where(x => f.Queue.Ids.ToArray().Contains(x.Id)).Select(x => x.CompanyId).Distinct().CountAsync());
        var rejected = await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query("backlog-0")));
        Assert.Equal(100, rejected.Result.Current);
    }

    [PostgresFact]
    public async Task Idempotency_PrecedesLimitsAndConcurrentDuplicatesConsumeOneAcceptance()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxPendingPerCompany = 1; f.Options.MaxAcceptedPerCompany24Hours = 1;
        var requestId = Guid.NewGuid();
        var accepted = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => f.Accept(Query(), requestId)));
        Assert.Single(accepted.Select(x => x.ExtractionId).Distinct());
        var repeated = await f.Accept(Query() with { Password = "changed", DownloadPolicy = DownloadPolicy.Refresh }, requestId);
        Assert.Equal(accepted[0].ExtractionId, repeated.ExtractionId);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => f.Accept(Query() with { Month = 6 }, requestId));
        await using var db = f.Db(); Assert.Equal(1, await db.Extractions.CountAsync()); Assert.Equal(1, await db.Dispatches.CountAsync());
        Assert.Equal(DownloadPolicy.ReuseValid, (await db.Extractions.SingleAsync()).DownloadPolicy);
    }

    [PostgresFact]
    public async Task RollingUsage_CountsFailedJobsAndReleasesAtTheExactBoundary()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxAcceptedPerCompany24Hours = 2;
        // Fixed whole-second timestamps preserve exact equality at PostgreSQL's microsecond precision.
        f.Clock.Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var start = f.Clock.Now;
        var first = await f.Accept(Query()); await f.Finish(first.ExtractionId, JobStatus.Failed);
        f.Clock.Now = start.AddHours(1);
        var second = await f.Accept(Query()); await f.Finish(second.ExtractionId);
        var rejected = await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query()));
        Assert.Equal(WorkloadLimitCode.CompanyUsageLimitReached, rejected.Result.Code);
        Assert.Equal(start.AddHours(24), rejected.Result.RetryAt);
        Assert.Equal(2, rejected.Result.Current);
        f.Options.MaxAcceptedPerCompany24Hours = 1;
        rejected = await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query()));
        Assert.Equal(start.AddHours(25), rejected.Result.RetryAt);
        f.Clock.Now = start.AddHours(25);
        await f.Accept(Query());
    }

    [PostgresFact]
    public async Task PendingIncludesRunningAndRetryingButExpiredWorkDoesNotOccupyCapacity()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxPendingPerCompany = 2;
        var first = await f.Accept(Query()); var second = await f.Accept(Query());
        await using var db = f.Db();
        await db.Extractions.Where(x => x.Id == first.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, JobStatus.Running));
        await db.Extractions.Where(x => x.Id == second.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, JobStatus.Retrying));
        await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => f.Accept(Query()));
        await db.Extractions.Where(x => x.Id == first.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, f.Clock.Now));
        await f.Accept(Query());
        await f.Finish(second.ExtractionId); await f.Accept(Query());
    }

    [PostgresFact]
    public async Task AdmissionCancellation_RollsBackAndDoesNotBlockAnotherCompany()
    {
        await using var f = await Fixture.CreateAsync();
        await using var blocker = f.Db(); await using var transaction = await blocker.Database.BeginTransactionAsync();
        var key = CompanyWorkload.LockKey("sri-company-admission:workload-a");
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var waiting = f.Accept(Query(), token: cancel.Token);
        await f.Accept(Query("workload-b"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await transaction.RollbackAsync(); await f.Accept(Query());
        await using var db = f.Db(); Assert.Equal(2, await db.Extractions.CountAsync()); Assert.Equal(2, await db.Dispatches.CountAsync());
    }

    [PostgresFact]
    public async Task FairDispatch_RotatesCompaniesAndKeepsUndispatchedWorkOutOfRecovery()
    {
        await using var f = await Fixture.CreateAsync();
        var a1 = await f.Accept(Query()); var a2 = await f.Accept(Query()); var b = await f.Accept(Query("workload-b"));
        await using var db = f.Db();
        // Old queued work is intentionally waiting for capacity, not orphaned Hangfire work.
        await db.Extractions.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddHours(-1)));
        await db.Extractions.Where(x => x.Id == a1.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddHours(-2)));
        await f.Dispatch(); await f.Dispatch();
        Assert.Equal(new[] { a1.ExtractionId }, f.Queue.Ids.ToArray());
        await f.Finish(a1.ExtractionId); await f.Dispatch();
        Assert.Equal(b.ExtractionId, f.Queue.Ids.Last());
        await f.Finish(b.ExtractionId); await f.Dispatch();
        Assert.Equal(a2.ExtractionId, f.Queue.Ids.Last()); Assert.Equal(3, f.Queue.Ids.Count);
    }

    [PostgresFact]
    public async Task ConcurrentDispatchers_RespectGlobalAndCompanyReservations()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.MaxDispatchedExtractions = 2;
        await f.Accept(Query()); await f.Accept(Query()); await f.Accept(Query("workload-b")); await f.Accept(Query("workload-c"));
        await Task.WhenAll(f.Dispatch(), f.Dispatch(), f.Dispatch());
        await using var db = f.Db();
        var companies = await (from d in db.Dispatches join e in db.Extractions on d.ExtractionId equals e.Id
            where d.DispatchedAt != null select e.CompanyId).ToArrayAsync();
        Assert.Equal(2, companies.Length); Assert.Equal(2, companies.Distinct().Count()); Assert.Equal(2, f.Queue.Ids.Count);
    }

    [PostgresFact]
    public async Task PublicationFailure_RollsBackReservationAndRemainsRecoverable()
    {
        await using var f = await Fixture.CreateAsync(); var accepted = await f.Accept(Query()); f.Queue.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => f.Dispatch());
        await using var db = f.Db(); Assert.Null((await db.Dispatches.SingleAsync()).DispatchedAt);
        f.Queue.Fail = false; await f.Dispatch();
        Assert.Equal(accepted.ExtractionId, Assert.Single(f.Queue.Ids));
        Assert.Equal(0, (await db.Extractions.SingleAsync()).AttemptCount);
    }

    [PostgresFact]
    public async Task ExpiredReservations_FreeDispatchCapacityAndPreserveHistory()
    {
        await using var f = await Fixture.CreateAsync(); var old = await f.Accept(Query()); await f.Dispatch();
        var next = await f.Accept(Query("workload-b")); await using var db = f.Db();
        await db.Extractions.Where(x => x.Id == old.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await f.Dispatch();
        Assert.Equal(next.ExtractionId, f.Queue.Ids.Last());
        var expired = await db.Extractions.SingleAsync(x => x.Id == old.ExtractionId);
        Assert.Equal(JobStatus.Failed, expired.JobStatus); Assert.Null(expired.EncryptedPassword);
        Assert.Single(await db.Failures.Where(x => x.ExtractionId == old.ExtractionId).ToListAsync());
    }

    [PostgresFact]
    public async Task WorkerCompanyExclusion_DefersWithoutAttemptAndReleasesAfterCompletion()
    {
        await using var f = await Fixture.CreateAsync();
        var a = await f.Accept(Query()); var b = await f.Accept(Query(taxpayer: "1790012345002"));
        var runner = new HoldingRunner(); var running = f.Worker(runner).ExecuteAsync(a.ExtractionId, default);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await f.Worker(new EmptyRunner()).ExecuteAsync(b.ExtractionId, default);
            await using var db = f.Db(); Assert.Equal(0, (await db.Extractions.SingleAsync(x => x.Id == b.ExtractionId)).AttemptCount);
            Assert.Equal(b.ExtractionId, Assert.Single(f.Queue.Ids));
        }
        finally { runner.Release.TrySetResult(); await running; }
        await f.Worker(new EmptyRunner()).ExecuteAsync(b.ExtractionId, default);
        await using var final = f.Db(); Assert.Equal(2, await final.Extractions.CountAsync(x => x.JobStatus == JobStatus.Completed));
    }

    [PostgresFact]
    public async Task SessionSlots_AreIndependentBetweenCompaniesAndReleasedOnDisconnect()
    {
        await using var f = await Fixture.CreateAsync();
        await using var first = new NpgsqlConnection(f.Connection + ";Pooling=false"); await first.OpenAsync();
        await using var second = new NpgsqlConnection(f.Connection + ";Pooling=false"); await second.OpenAsync();
        Assert.True(await CompanyWorkload.TryAcquireExecutionSlotAsync(first, "a", 1, default));
        Assert.False(await CompanyWorkload.TryAcquireExecutionSlotAsync(second, "a", 1, default));
        Assert.True(await CompanyWorkload.TryAcquireExecutionSlotAsync(second, "b", 1, default));
        await first.CloseAsync();
        // Socket closure and backend lock cleanup need not complete in the same scheduler turn.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await CompanyWorkload.TryAcquireExecutionSlotAsync(second, "a", 1, deadline.Token))
            await Task.Delay(20, deadline.Token);
    }

    [PostgresFact]
    public async Task HttpAdmission_ReturnsTyped429WithoutNewHeadersAndPreservesIdempotency()
    {
        await using var f = await Fixture.CreateAsync();
        await using var original = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ExtractionJobs:ConnectionString"] = f.Connection,
                ["ExtractionJobs:MaxPendingPerCompany"] = "1", ["ExtractionJobs:MaxAcceptedPerCompany24Hours"] = "1" })));
        using var admin = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        admin.DefaultRequestHeaders.Add("XApiKey", original.Admin.Token);
        var create = await admin.PostAsJsonAsync("/api/admin/companies/workload-a/api-keys", new { name = "workload fixture" });
        using var key = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("XApiKey", key.RootElement.GetProperty("apiKey").GetString());
        var idempotency = Guid.NewGuid();
        object Body(Guid id, string mode = "async") => new { companyId = "workload-a", user = "1790012345001", password = "fixture-only",
            year = 2026, month = 5, day = 0, documentType = "invoice", downloadFormat = "xml", clientRequestId = id, executionMode = mode };
        var first = await client.PostAsJsonAsync("/api/received-documents/query", Body(idempotency));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/received-documents/query", Body(idempotency))).StatusCode);
        foreach (var mode in new[] { "async", "sync" })
        {
            var rejected = await client.PostAsJsonAsync("/api/received-documents/query", Body(Guid.NewGuid(), mode));
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode); Assert.False(rejected.Headers.Contains("Retry-After"));
            var response = (await rejected.Content.ReadFromJsonAsync<WorkloadLimitResponse>())!;
            Assert.Equal(WorkloadLimitCode.CompanyPendingLimitReached, response.Code); Assert.Equal(1, response.Limit); Assert.Null(response.RetryAt);
        }
        await using var db = f.Db(); Assert.Equal(1, await db.Extractions.CountAsync()); Assert.Equal(1, await db.Dispatches.CountAsync());
    }

    [PostgresFact]
    public async Task RealHangfire_CompletesAcceptedWorkAndRotatesCompaniesWithoutDuplicateAttempts()
    {
        await using var f = await Fixture.CreateAsync(realHangfire: true);
        var a1 = await f.Accept(Query()); f.Clock.Now = f.Clock.Now.AddMilliseconds(1);
        var a2 = await f.Accept(Query()); f.Clock.Now = f.Clock.Now.AddMilliseconds(1);
        var b = await f.Accept(Query("workload-b"));
        var runner = f.Services.GetRequiredService<OrderedRunner>();
        var storage = f.Services.GetRequiredService<JobStorage>();
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions
        {
            WorkerCount = 2, Activator = new ScopedActivator(f.Services.GetRequiredService<IServiceScopeFactory>()),
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        }, storage);
        await f.Dispatch();
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await f.Dispatch();
            await using var db = f.Db();
            Assert.Equal(1, await db.Dispatches.CountAsync(x => x.DispatchedAt != null));
            Assert.Equal(1, await db.Attempts.CountAsync());
        }
        finally { runner.Release.TrySetResult(); }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var job in new[] { a1, b, a2 })
        {
            while (true)
            {
                await using var db = f.Db();
                var state = await db.Extractions.Where(x => x.Id == job.ExtractionId).Select(x => x.JobStatus).SingleAsync(deadline.Token);
                Assert.NotEqual(JobStatus.Failed, state);
                if (state == JobStatus.Completed)
                {
                    var deliveryId = await db.Dispatches.Where(x => x.ExtractionId == job.ExtractionId)
                        .Select(x => x.HangfireJobId).SingleAsync(deadline.Token);
                    using var delivery = storage.GetConnection();
                    if (delivery.GetJobData(deliveryId!).State == SucceededState.StateName) break;
                }
                await Task.Delay(50, deadline.Token);
            }
            await f.Dispatch();
        }
        Assert.Equal(new[] { "workload-a", "workload-b", "workload-a" }, runner.Companies.ToArray());
        await using var final = f.Db();
        Assert.Equal(3, await final.Attempts.CountAsync());
        Assert.All(await final.Extractions.ToListAsync(), x => { Assert.Equal(1, x.AttemptCount); Assert.Null(x.EncryptedPassword); });
        Assert.Equal(3, await final.Dispatches.CountAsync(x => x.HangfireJobId != null));
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Queue : IBackgroundJobClient
    {
        public ConcurrentQueue<Guid> Ids { get; } = new();
        public bool Fail { get; set; }
        public string Create(Job job, IState state)
        { if (Fail) throw new IOException("Synthetic publication failure."); Ids.Enqueue((Guid)job.Args[0]); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => throw new NotSupportedException();
    }
    private class EmptyRunner : IReceivedExtractionRunner
    {
        public virtual Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            var result = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true };
            result.Pagination.Status = PaginationStatus.Completed; return Task.FromResult(result);
        }
    }
    private sealed class HoldingRunner : EmptyRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        { Started.TrySetResult(); await Release.Task.WaitAsync(token); return await base.RunAsync(query, progress, token); }
    }
    private sealed class OrderedRunner : EmptyRunner
    {
        public ConcurrentQueue<string> Companies { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;
        public override async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            Companies.Enqueue(query.CompanyId);
            if (Interlocked.Increment(ref calls) == 1) { Started.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await base.RunAsync(query, progress, token);
        }
    }
    private sealed class ScopedActivator(IServiceScopeFactory factory) : JobActivator
    {
        public override JobActivatorScope BeginScope(JobActivatorContext context) => new ScopedActivation(factory.CreateScope());
        private sealed class ScopedActivation(IServiceScope scope) : JobActivatorScope
        {
            public override object Resolve(Type type) => scope.ServiceProvider.GetRequiredService(type);
            public override void DisposeScope() => scope.Dispose();
        }
    }
    private sealed class UnusedStorage : IDocumentStorage
    {
        public bool CanRead(DocumentStorageReference reference) => false;
        public Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required string Connection { get; init; }
        public required string Database { get; init; }
        public required ServiceProvider Services { get; init; }
        public FixedClock Clock { get; } = new();
        public Queue Queue => Services.GetRequiredService<Queue>();
        public ExtractionJobOptions Options => Services.GetRequiredService<IOptions<ExtractionJobOptions>>().Value;
        public CrawlerDbContext Db() => Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContext();
        public static async Task<Fixture> CreateAsync(bool realHangfire = false)
        {
            var name = "workload_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")); await admin.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await command.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")) { Database = name }.ConnectionString;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["ExtractionJobs:ConnectionString"] = connection, ["ExtractionJobs:EncryptionKey"] = Convert.ToBase64String(new byte[32]) }).Build();
            var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(config); services.AddExtractionJobs(config);
            if (!realHangfire)
            { services.RemoveAll<IBackgroundJobClient>(); services.AddSingleton<Queue>(); services.AddSingleton<IBackgroundJobClient>(p => p.GetRequiredService<Queue>()); }
            services.AddSingleton<OrderedRunner>();
            services.AddSingleton<IReceivedExtractionRunner>(p => p.GetRequiredService<OrderedRunner>());
            services.AddScoped<ExtractionWorker>();
            services.AddSingleton<IDocumentStorage, UnusedStorage>();
            services.AddScoped<IDocumentParser, DescagaCompronanteSRI.Services.ReceivedDocuments.DocumentParser>();
            var result = new Fixture { Database = name, Connection = connection, Services = services.BuildServiceProvider() };
            await using var db = result.Db(); await db.Database.MigrateAsync(); return result;
        }
        public async Task<ExtractionAccepted> Accept(ReceivedDocumentsQuery query, Guid? id = null, CancellationToken token = default)
        {
            await using var db = Db();
            return await new ExtractionJobs(db, Services.GetRequiredService<CredentialCipher>(), Microsoft.Extensions.Options.Options.Create(Options), clock: Clock).AcceptAsync(query, id, token);
        }
        public Task Dispatch() => new JobDispatcher(Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(Options), NullLogger<JobDispatcher>.Instance).DispatchOnceAsync(default);
        public async Task Finish(Guid id, JobStatus status = JobStatus.Completed)
        { await using var db = Db(); await db.Extractions.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, status).SetProperty(x => x.FinishedAt, Clock.Now)); }
        public ExtractionWorker Worker(IReceivedExtractionRunner runner) => new(Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            Services.GetRequiredService<CredentialCipher>(), Services.GetRequiredService<ISriDocumentJsonParser>(), Services.GetRequiredService<IDocumentStorage>(),
            Queue, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<ExtractionWorker>.Instance, Services.GetRequiredService<IDocumentValidator>(), Services.GetRequiredService<IOptions<DocumentValidationOptions>>());
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")); await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {Database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
