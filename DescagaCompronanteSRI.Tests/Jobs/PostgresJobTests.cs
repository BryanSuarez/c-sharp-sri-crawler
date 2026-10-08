using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DescagaCompronanteSRI.Tests.Jobs;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES") is null)
            Skip = "Set SRI_TEST_POSTGRES to an isolated PostgreSQL test database.";
    }
}
public sealed class ParserIntegrationFactAttribute : FactAttribute
{
    public ParserIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES") is null || Environment.GetEnvironmentVariable("SRI_TEST_PARSER") is null)
            Skip = "Set SRI_TEST_POSTGRES and SRI_TEST_PARSER for the real HTTP parser integration.";
    }
}
[Collection("HangfireHost")]
public sealed class PostgresJobTests
{
    private static string Connection => Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES")!;
    private static ExtractionJobOptions OptionsValue => new() { ConnectionString = Connection,
        EncryptionKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray()), ParserUrl = Environment.GetEnvironmentVariable("SRI_TEST_PARSER") ?? "http://localhost:3007" };
    private static ReceivedDocumentsQuery Query(string? company = null, string user = "1790012345001", DownloadFormat format = DownloadFormat.Xml) => new()
    {
        CompanyId = company ?? "test-" + Guid.NewGuid().ToString("N"), User = user, Password = "test-sensitive-password",
        Year = 2026, Month = 9, Day = 0, DocumentType = DocumentType.Invoice, DownloadFormat = format
    };
    private static async Task<ServiceProvider> Provider(ISriDocumentJsonParser? parser = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExtractionJobs:ConnectionString"] = Connection, ["ExtractionJobs:EncryptionKey"] = OptionsValue.EncryptionKey,
            ["ExtractionJobs:ParserUrl"] = OptionsValue.ParserUrl
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IConfiguration>(config);
        services.AddExtractionJobs(config);
        if (parser is not null) services.AddSingleton(parser);
        services.AddSingleton<IDocumentStorage, MemoryStorage>();
        var provider = services.BuildServiceProvider();
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
        return provider;
    }
    private static async Task<(Guid Id, Guid Attempt)> Start(ServiceProvider provider, ReceivedDocumentsQuery query)
    {
        using var scope = provider.CreateScope();
        var accepted = await scope.ServiceProvider.GetRequiredService<IExtractionJobs>().AcceptAsync(query, null, default);
        var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var record = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
        record.JobStatus = JobStatus.Running; record.AttemptCount = 1; record.ActiveAttemptId = Guid.NewGuid();
        db.Attempts.Add(new() { Id = record.ActiveAttemptId.Value, ExtractionId = record.Id, Number = 1, StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (record.Id, record.ActiveAttemptId.Value);
    }
    private static PersistentExtractionProgress Progress(ServiceProvider provider, Guid id, Guid attempt, ReceivedDocumentsQuery query) => new(
        provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), provider.GetRequiredService<ISriDocumentJsonParser>(),
        provider.GetRequiredService<IDocumentStorage>(), id, attempt, query);
    private static ReceivedDocumentResponse Document(string key, DownloadFormat format = DownloadFormat.Xml) => new()
    {
        Metadata = new() { AuthorizationNumber = key }, DownloadFormat = format,
        DownloadStatus = DocumentDownloadStatus.Downloaded, ParseStatus = format == DownloadFormat.Xml ? DocumentParseStatus.Parsed : DocumentParseStatus.NotApplicable,
        Storage = new(StorageProvider.Local, null, "test/key.xml"), SourceXml = "fixture", XmlHash = "fixture-hash"
    };

    [PostgresFact]
    public async Task Acceptance_IsAtomicIdempotentAndDoesNotPersistPlainPassword()
    {
        await using var provider = await Provider(new StubParser());
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var query = Query(); var clientId = Guid.NewGuid();
        var first = await jobs.AcceptAsync(query, clientId, default);
        var second = await jobs.AcceptAsync(query with { Password = "changed-password" }, clientId, default);
        Assert.Equal(first.ExtractionId, second.ExtractionId);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => jobs.AcceptAsync(query with { Month = 8 }, clientId, default));
        var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var row = await db.Extractions.SingleAsync(x => x.Id == first.ExtractionId);
        Assert.DoesNotContain(query.Password, row.QueryJson);
        Assert.DoesNotContain(query.Password, row.EncryptedPassword!);
        Assert.Equal(query.Password, provider.GetRequiredService<CredentialCipher>().Decrypt(row.EncryptedPassword!, row.Id));
        Assert.Single(await db.Dispatches.Where(x => x.ExtractionId == row.Id).ToListAsync());
        Assert.Null(await jobs.GetAsync(row.Id, "another-company", default));
        Assert.Null(await jobs.DocumentsAsync(row.Id, "another-company", 0, 100, default));
    }
    [PostgresFact]
    public async Task Persistence_PreservesOwnerAndVersionsAndPdfDoesNotEraseXml()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query(); var first = await Start(provider, query);
        var key = new string('1', 49);
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        var pdfQuery = query with { DownloadFormat = DownloadFormat.Pdf };
        var second = await Start(provider, pdfQuery);
        await Progress(provider, second.Id, second.Attempt, pdfQuery).SaveAsync(Document(key, DownloadFormat.Pdf), default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var owner = await db.Documents.SingleAsync(x => x.CompanyId == query.CompanyId);
        Assert.Equal(query.User, owner.TaxpayerId);
        Assert.Equal("1719956854001", owner.IssuerTaxpayerId);
        Assert.Equal(2, await db.Files.CountAsync(x => x.DocumentId == owner.Id));
        Assert.Equal(2, await db.Conversions.CountAsync(x => x.DocumentId == owner.Id));
        Assert.Single(await db.Results.Where(x => x.ExtractionId == first.Id).ToListAsync());
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var list = await jobs.DocumentsAsync(first.Id, query.CompanyId, 0, 1, default);
        var item = list!.Items.Single();
        Assert.False(item.TryGetProperty("documentJson", out _));
        var detail = await jobs.DocumentAsync(first.Id, query.CompanyId, item.GetProperty("documentId").GetInt64(), default);
        Assert.Equal(key, detail!.Value.GetProperty("documentJson").GetProperty("infoTributaria").GetProperty("claveAcceso").GetString());
        Assert.Null(await jobs.DocumentAsync(first.Id, "another-company", item.GetProperty("documentId").GetInt64(), default));
    }
    [PostgresFact]
    public async Task Retry_ReusesSavedDocumentsAndFlagsUnseenPreviousDocuments()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query(); var run = await Start(provider, query); var key = new string('2', 49);
        await Progress(provider, run.Id, run.Attempt, query).SaveAsync(Document(key), default);
        var nextAttempt = Guid.NewGuid();
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        await db.Extractions.Where(x => x.Id == run.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveAttemptId, nextAttempt));
        var progress = Progress(provider, run.Id, nextAttempt, query);
        Assert.True(await progress.HasUnseenDocumentsAsync(default));
        var saved = await progress.FindSavedAsync(key, 2, 3, default);
        Assert.NotNull(saved); Assert.Equal(2, saved.PageNumber); Assert.Equal(3, saved.RowIndex);
        Assert.False(await progress.HasUnseenDocumentsAsync(default));
        Assert.Single(await db.Results.Where(x => x.ExtractionId == run.Id).ToListAsync());
    }
    [PostgresFact]
    public async Task ParserFailure_PreservesDownloadAndOldConversion()
    {
        var parser = new StubParser();
        await using var provider = await Provider(parser);
        var query = Query(); var first = await Start(provider, query); var key = new string('3', 49);
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        parser.Fail = true;
        var second = await Start(provider, query);
        await Progress(provider, second.Id, second.Attempt, query).SaveAsync(Document(key), default);
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var summary = await jobs.GetAsync(second.Id, query.CompanyId, default);
        Assert.Equal(1, summary!.DownloadedCount); Assert.Equal(0, summary.FailedCount); Assert.Equal(1, summary.ConversionFailedCount);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        Assert.Equal(2, await db.Conversions.CountAsync(x => x.DocumentId == db.Documents.Where(d => d.CompanyId == query.CompanyId).Select(d => d.Id).Single()));
        Assert.Single(await db.Failures.Where(x => x.ExtractionId == second.Id).ToListAsync());
    }
    [PostgresFact]
    public async Task Worker_GuardsDuplicateDeliveryAndClearsCredentials()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var accepted = await jobs.AcceptAsync(query, null, default);
        var runner = new StubRunner();
        var worker = new ExtractionWorker(provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(), provider.GetRequiredService<IDocumentStorage>(),
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance);
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        Assert.Equal(1, runner.Calls);
        var summary = await jobs.GetAsync(accepted.ExtractionId, query.CompanyId, default);
        Assert.Equal(JobStatus.Completed, summary!.JobStatus); Assert.Equal(ExtractionStatus.Completed, summary.ExtractionStatus);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        Assert.Null((await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId)).EncryptedPassword);
    }
    [PostgresFact]
    public async Task Dispatcher_EnqueuesOnceAndExpiresCredentials()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var accepted = await scope.ServiceProvider.GetRequiredService<IExtractionJobs>().AcceptAsync(query, null, default);
        var dispatcher = new JobDispatcher(provider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<JobDispatcher>.Instance);
        await dispatcher.DispatchOnceAsync(default);
        await dispatcher.DispatchOnceAsync(default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var outbox = await db.Dispatches.SingleAsync(x => x.ExtractionId == accepted.ExtractionId);
        Assert.NotNull(outbox.HangfireJobId); Assert.NotNull(outbox.DispatchedAt);
        await db.Extractions.Where(x => x.Id == accepted.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await dispatcher.DispatchOnceAsync(default);
        var expired = await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == accepted.ExtractionId);
        Assert.Null(expired.EncryptedPassword); Assert.Equal(JobStatus.Failed, expired.JobStatus);
    }

    [PostgresFact]
    public async Task ShutdownDuringLastRetry_PreservesCredentialsAndCanResume()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var accepted = await jobs.AcceptAsync(query, null, default);
        var factory = provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var extraction = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
            extraction.AttemptCount = 2;
            for (var n = 1; n <= 2; n++) db.Attempts.Add(new() { Id = Guid.NewGuid(), ExtractionId = extraction.Id,
                Number = n, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow, ErrorCode = "interrupted" });
            await db.SaveChangesAsync();
        }
        using var stop = new CancellationTokenSource();
        ExtractionWorker Worker(IReceivedExtractionRunner runner) => new(factory, runner,
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(),
            provider.GetRequiredService<IDocumentStorage>(), provider.GetRequiredService<IBackgroundJobClient>(),
            Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Worker(new ShutdownRunner(stop)).ExecuteAsync(accepted.ExtractionId, stop.Token));
        await using (var db = await factory.CreateDbContextAsync())
        {
            var extraction = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
            Assert.Equal(JobStatus.Retrying, extraction.JobStatus);
            Assert.NotNull(extraction.EncryptedPassword);
            Assert.Equal("shutdown", (await db.Attempts.SingleAsync(x => x.Number == 3 && x.ExtractionId == extraction.Id)).ErrorCode);
        }
        await Worker(new StubRunner()).ExecuteAsync(accepted.ExtractionId, default);
        var result = await jobs.GetAsync(accepted.ExtractionId, query.CompanyId, default);
        Assert.Equal(JobStatus.Completed, result!.JobStatus);
        Assert.Equal(4, result.Attempts);
        Assert.Equal(1, result.DownloadedCount);
    }
    private sealed class ShutdownRunner(CancellationTokenSource stop) : IReceivedExtractionRunner
    {
        public Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            stop.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Shutdown was not propagated.");
        }
    }
    [ParserIntegrationFact]
    public async Task RealHttpParser_ValidatesIdentityAndSignatureRemoval()
    {
        var options = Microsoft.Extensions.Options.Options.Create(OptionsValue);
        using var client = new HttpClient { BaseAddress = new Uri(OptionsValue.ParserUrl), Timeout = Timeout.InfiniteTimeSpan };
        var parser = new HttpSriDocumentJsonParser(client, options);
        var key = new string('1', 49);
        var xml = $"<factura id='comprobante' version='1.1.0'><infoTributaria><ambiente>2</ambiente><tipoEmision>1</tipoEmision><ruc>1719956854001</ruc><claveAcceso>{key}</claveAcceso></infoTributaria><infoFactura><tipoIdentificacionComprador>05</tipoIdentificacionComprador><totalConImpuestos><totalImpuesto><codigo>2</codigo><codigoPorcentaje>4</codigoPorcentaje><valor>1</valor></totalImpuesto></totalConImpuestos></infoFactura><detalles><detalle><descripcion>Fixture</descripcion></detalle></detalles><ds:Signature xmlns:ds='http://www.w3.org/2000/09/xmldsig#'>secret-signature</ds:Signature></factura>";
        var result = await parser.ParseAsync(xml, DocumentType.Invoice, key, default);
        Assert.Equal(DocumentParseStatus.Parsed, result.Status);
        Assert.DoesNotContain("secret-signature", result.DocumentJson!.Value.GetRawText());
        Assert.Equal(DocumentParseStatus.Failed, (await parser.ParseAsync(xml, DocumentType.Invoice, new string('2', 49), default)).Status);
        Assert.Equal(DocumentParseStatus.Unsupported, (await parser.ParseAsync(xml, DocumentType.RemissionGuide, key, default)).Status);
    }

    [PostgresFact]
    public async Task HangfireServer_PerformsPersistedJobUsingDependencyInjection()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var accepted = await jobs.AcceptAsync(query, null, default);
        var storage = provider.GetRequiredService<JobStorage>();
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions
        {
            WorkerCount = 1, Queues = ["fixture"],
            Activator = new FixtureActivator(provider), ShutdownTimeout = TimeSpan.FromSeconds(5)
        }, storage);
        provider.GetRequiredService<IBackgroundJobClient>().Create(
            Hangfire.Common.Job.FromExpression<ExtractionWorker>(x => x.ExecuteAsync(accepted.ExtractionId, CancellationToken.None)),
            new Hangfire.States.EnqueuedState("fixture"));
        ExtractionSummary? summary = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            summary = await jobs.GetAsync(accepted.ExtractionId, query.CompanyId, timeout.Token);
            if (summary!.JobStatus is JobStatus.Completed or JobStatus.Failed) break;
            await Task.Delay(100, timeout.Token);
        }
        Assert.Equal(JobStatus.Completed, summary!.JobStatus);
        Assert.Equal(1, summary.DownloadedCount);
    }
    [PostgresFact]
    public async Task AccountLock_DefersWorkWithoutConsumingAnAttempt()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var accepted = await scope.ServiceProvider.GetRequiredService<IExtractionJobs>().AcceptAsync(query, null, default);
        await using var lockConnection = new NpgsqlConnection(Connection);
        await lockConnection.OpenAsync();
        var key = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("sri-taxpayer:" + query.User)), 0);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", lockConnection);
        command.Parameters.AddWithValue("key", key); await command.ExecuteNonQueryAsync();
        var runner = new StubRunner();
        var worker = new ExtractionWorker(provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(), provider.GetRequiredService<IDocumentStorage>(),
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance);
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        Assert.Equal(0, runner.Calls);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, (await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId)).AttemptCount);
    }
    private sealed class FixtureActivator(ServiceProvider provider) : JobActivator
    {
        public override object ActivateJob(Type type) => new ExtractionWorker(provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), new StubRunner(),
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(), provider.GetRequiredService<IDocumentStorage>(),
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance);
    }


    [PostgresFact]
    public async Task InterruptedAttempt_PreservesFilesAndRecoversWithANewAttempt()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query();
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var accepted = await jobs.AcceptAsync(query, null, default);
        var runner = new InterruptibleRunner();
        var worker = new ExtractionWorker(provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(), provider.GetRequiredService<IDocumentStorage>(),
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance);
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        var pending = await jobs.GetAsync(accepted.ExtractionId, query.CompanyId, default);
        Assert.Equal(JobStatus.Retrying, pending!.JobStatus); Assert.Equal(1, pending.DownloadedCount);
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        var finished = await jobs.GetAsync(accepted.ExtractionId, query.CompanyId, default);
        Assert.Equal(JobStatus.Completed, finished!.JobStatus); Assert.Equal(2, finished.Attempts);
        Assert.Equal(1, finished.DownloadedCount); Assert.Equal(1, runner.Downloads);
    }
    private sealed class InterruptibleRunner : IReceivedExtractionRunner
    {
        private bool interrupted;
        public int Downloads { get; private set; }
        public async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            var key = new string('6', 49);
            var saved = await progress.FindSavedAsync(key, 1, 0, token);
            if (saved is null) { Downloads++; await progress.SaveAsync(Document(key), token); }
            if (!interrupted) { interrupted = true; throw new IOException("Simulated worker interruption"); }
            var result = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true };
            result.Pagination.Status = PaginationStatus.Completed;
            return result;
        }
    }


    [PostgresFact]
    public async Task SlidingInvisibility_KeepsLongJobOwnedBeyondItsVisibilityTimeout()
    {
        await using var provider = await Provider(new StubParser());
        _ = provider.GetRequiredService<IBackgroundJobClient>();
        var storageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire", UseSlidingInvisibilityTimeout = true,
            InvisibilityTimeout = TimeSpan.FromSeconds(3), QueuePollInterval = TimeSpan.FromMilliseconds(100)
        };
        var storage = new PostgreSqlStorage(new Hangfire.PostgreSql.Factories.NpgsqlConnectionFactory(Connection, storageOptions, null), storageOptions);
        var queue = "sliding_" + Guid.NewGuid().ToString("N");
        using var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 2, Queues = [queue], Activator = new JobActivator() }, storage);
        var id = Guid.NewGuid();
        var client = new BackgroundJobClient(storage);
        var job = client.Create(Hangfire.Common.Job.FromExpression(() => SlowJob.RunAsync(id)), new Hangfire.States.EnqueuedState(queue));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            var state = storage.GetMonitoringApi().JobDetails(job)?.History.FirstOrDefault()?.StateName;
            if (state == "Succeeded") break;
            await Task.Delay(100, timeout.Token);
        }
        Assert.Equal(1, SlowJob.Calls[id]);
        Assert.Equal("Succeeded", storage.GetMonitoringApi().JobDetails(job).History.First().StateName);
    }
    public static class SlowJob
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> Calls = new();
        public static async Task RunAsync(Guid id)
        {
            Calls.AddOrUpdate(id, 1, (_, count) => count + 1);
            await Task.Delay(TimeSpan.FromSeconds(7));
        }
    }

    private sealed class StubParser : ISriDocumentJsonParser
    {
        public bool Fail { get; set; }
        public Task<JsonConversion> ParseAsync(string xml, DocumentType type, string key, CancellationToken token) =>
            Task.FromResult(Fail ? new JsonConversion(DocumentParseStatus.Failed, ErrorCode: "conversionFailed")
                : new JsonConversion(DocumentParseStatus.Parsed, JsonSerializer.SerializeToElement(new { infoTributaria = new { ruc = "1719956854001", claveAcceso = key } }), "fixture", "1.0"));
    }
    private sealed class MemoryStorage : IDocumentStorage
    {
        public Task<Models.Storage.DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("fixture")));
    }
    private sealed class StubRunner : IReceivedExtractionRunner
    {
        public int Calls { get; private set; }
        public async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            Calls++;
            await progress.SaveAsync(Document(new string('4', 49)), token);
            var result = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true };
            result.Pagination.Status = PaginationStatus.Completed; result.Pagination.PagesProcessed = 1;
            return result;
        }
    }
}
