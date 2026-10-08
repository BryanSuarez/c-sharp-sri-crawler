using System.Security.Cryptography;
using DescagaCompronanteSRI.Validation;
using DescagaCompronanteSRI.Services.Parsing;
using DescagaCompronanteSRI.Tests.Validation;
using System.Text.Json.Nodes;
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
        provider.GetRequiredService<IDocumentStorage>(), id, attempt, query, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
    private static ReceivedDocumentResponse Document(string key, DownloadFormat format = DownloadFormat.Xml) => new()
    {
        Metadata = new() { AuthorizationNumber = key }, DownloadFormat = format,
        DownloadStatus = DocumentDownloadStatus.Downloaded, ParseStatus = format == DownloadFormat.Xml ? DocumentParseStatus.Parsed : DocumentParseStatus.NotApplicable,
        StorageStatus = DocumentStorageStatus.Stored,
        Validation = new() { Status = DocumentValidationStatus.Valid, ValidatorVersion = "1", Sha256 = "fixture-hash", SizeBytes = 7 },
        ParsedDocument = new { receipt = key }, Storage = new(StorageProvider.Local, null, "test/key.xml") { Revision = "fixture-revision" }, SourceXml = "fixture", XmlHash = "fixture-hash"
    };

    [PostgresFact]
    public async Task MetadataAndValidation_AreTypedPersistentAndCountedWithoutReadingJson()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query(); var run = await Start(provider, query);
        var result = Document(new string('6', 49));
        result.StorageStatus = DocumentStorageStatus.NotAttempted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Progress(provider, run.Id, run.Attempt, query).SaveAsync(result, default));
        result.StorageStatus = DocumentStorageStatus.Stored;
        result.Metadata = new ReceivedDocumentMetadataParser(Microsoft.Extensions.Options.Options.Create(new DocumentValidationOptions()))
            .Parse(result.Metadata!, new("invalid", "0", "1234.56", "01/09/2026", "01/09/2026 12:30:00"));
        result.Errors.AddRange(result.Metadata.Errors);
        await Progress(provider, run.Id, run.Attempt, query).SaveAsync(result, default);
        var failed = Document(new string('7', 49));
        failed.DownloadStatus = DocumentDownloadStatus.Failed; failed.Storage = null; failed.StorageStatus = DocumentStorageStatus.NotAttempted;
        failed.Validation = new() { Status = DocumentValidationStatus.Invalid, Errors = [new(ExtractionErrorCode.InvalidDocument, "Invalid fixture.")] };
        await Progress(provider, run.Id, run.Attempt, query).SaveAsync(failed, default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var row = await db.Results.SingleAsync(x => x.ExtractionId == run.Id && x.Identity == new string('6', 49));
        Assert.Null(row.Amount); Assert.Equal(0m, row.Taxes); Assert.Equal(1234.56m, row.Total);
        Assert.Equal(new DateOnly(2026, 9, 1), row.IssuedDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 17, 30, 0, TimeSpan.Zero), row.AuthorizedAt);
        Assert.Contains("invalid", row.SourceValuesJson!);
        Assert.Equal(DocumentValidationStatus.Valid, row.ValidationStatus);
        var file = await db.Files.SingleAsync(x => x.Id == row.FileId);
        Assert.Equal("fixture-hash", file.Sha256);
        using var scope = provider.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var summary = await jobs.GetAsync(run.Id, query.CompanyId, default);
        Assert.Equal(1, summary!.ValidationIssueCount); Assert.Equal(1, summary.MetadataIssueCount);
        Assert.Equal(summary.DiscoveredCount, summary.DownloadedCount + summary.FailedCount);
        var listed = await jobs.DocumentsAsync(run.Id, query.CompanyId, 0, 100, default);
        Assert.Equal("valid", listed!.Items[0].GetProperty("validation").GetProperty("status").GetString());
        Assert.Equal("partial", listed.Items[0].GetProperty("metadataParseStatus").GetString());
    }
    [PostgresFact]
    public async Task Recovery_RevalidatesLegacyFilesAndRejectsInvalidStoredContent()
    {
        await using var provider = await Provider(new StubParser());
        var xml = DocumentValidatorTests.Fixture(); var key = DocumentValidatorTests.AccessKey(xml);
        var query = Query(); var run = await Start(provider, query);
        await Progress(provider, run.Id, run.Attempt, query).SaveAsync(Document(key), default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var row = await db.Results.SingleAsync(x => x.ExtractionId == run.Id);
        var old = JsonNode.Parse(row.ResponseJson)!.AsObject(); old.Remove("validation"); old.Remove("storageStatus");
        row.ResponseJson = old.ToJsonString(); row.ValidationStatus = DocumentValidationStatus.NotChecked; row.FileHash = null;
        var file = await db.Files.SingleAsync(x => x.Id == row.FileId); file.Sha256 = null;
        await db.SaveChangesAsync();
        var storage = (MemoryStorage)provider.GetRequiredService<IDocumentStorage>(); storage.Bytes = Encoding.UTF8.GetBytes(xml);
        var recovered = await Progress(provider, run.Id, run.Attempt, query).FindSavedAsync(key, 2, 4, default);
        Assert.NotNull(recovered); Assert.Equal(DocumentValidationStatus.Valid, recovered.Validation.Status);
        Assert.Equal(MetadataParseStatus.NotChecked, recovered.MetadataParseStatus);
        Assert.Null(recovered.Metadata!.SourceValues);
        Assert.Equal(1, storage.Reads);
        Assert.NotNull(await Progress(provider, run.Id, run.Attempt, query).FindSavedAsync(key, 2, 4, default));
        Assert.Equal(1, storage.Reads);
        await db.Entry(row).ReloadAsync(); row.ResponseJson = old.ToJsonString();
        await db.SaveChangesAsync(); storage.Bytes = "<html>error</html>"u8.ToArray();
        Assert.Null(await Progress(provider, run.Id, run.Attempt, query).FindSavedAsync(key, 2, 4, default));
        await db.Entry(file).ReloadAsync();
        Assert.Equal(recovered.Validation.Sha256, file.Sha256);
    }
    [PostgresFact]
    public async Task Reprocessor_ValidatesWithoutMutatingHistoricalResultsOrErasingConversions()
    {
        await using var provider = await Provider(new StubParser());
        var xml = DocumentValidatorTests.Fixture(); var key = DocumentValidatorTests.AccessKey(xml);
        var query = Query(); var run = await Start(provider, query);
        await Progress(provider, run.Id, run.Attempt, query).SaveAsync(Document(key), default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var row = await db.Results.SingleAsync(x => x.ExtractionId == run.Id); var historical = row.ResponseJson;
        var storage = (MemoryStorage)provider.GetRequiredService<IDocumentStorage>(); storage.Bytes = Encoding.UTF8.GetBytes(xml);
        using var scope = provider.CreateScope(); var reprocessor = scope.ServiceProvider.GetRequiredService<DocumentReprocessor>();
        await reprocessor.ReprocessAsync(row.FileId!.Value, query.CompanyId, query.User, default);
        Assert.Equal(2, await db.Conversions.CountAsync(x => x.DocumentId == row.DocumentId));
        storage.Bytes = "<html>error</html>"u8.ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reprocessor.ReprocessAsync(row.FileId.Value, query.CompanyId, query.User, default));
        Assert.Equal(2, await db.Conversions.CountAsync(x => x.DocumentId == row.DocumentId));
        await db.Entry(row).ReloadAsync(); Assert.Equal(historical, row.ResponseJson);
    }

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
        Assert.Equal(1, await db.Conversions.CountAsync(x => x.DocumentId == owner.Id));
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
        var changed = Document(key);
        changed.XmlHash = "different-valid-bytes";
        changed.Validation = changed.Validation with { Sha256 = changed.XmlHash };
        await Progress(provider, second.Id, second.Attempt, query).SaveAsync(changed, default);
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
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
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
            Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
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
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
        await worker.ExecuteAsync(accepted.ExtractionId, default);
        Assert.Equal(0, runner.Calls);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        Assert.Equal(0, (await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId)).AttemptCount);
    }
    private sealed class FixtureActivator(ServiceProvider provider) : JobActivator
    {
        public override object ActivateJob(Type type) => new ExtractionWorker(provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), new StubRunner(),
            provider.GetRequiredService<CredentialCipher>(), provider.GetRequiredService<ISriDocumentJsonParser>(), provider.GetRequiredService<IDocumentStorage>(),
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
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
            provider.GetRequiredService<IBackgroundJobClient>(), Microsoft.Extensions.Options.Options.Create(OptionsValue), NullLogger<ExtractionWorker>.Instance, provider.GetRequiredService<IDocumentValidator>(), provider.GetRequiredService<IOptions<DocumentValidationOptions>>());
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

    [PostgresFact]
    public async Task IndependentExtraction_ReusesFileAndConversionWithCurrentMetadataAndNoRead()
    {
        var parser = new StubParser(); await using var provider = await Provider(parser);
        var query = Query(); var key = new string('7', 49); var first = await Start(provider, query);
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        var second = await Start(provider, query);
        var progress = Progress(provider, second.Id, second.Attempt, query);
        await progress.PreparePageAsync([key], default);
        var metadata = new ReceivedDocumentMetadata { AuthorizationNumber = key, Amount = 45.67m, IssuedDate = new(2026, 9, 12),
            MetadataParseStatus = MetadataParseStatus.Partial, Errors = [new(ExtractionErrorCode.InvalidMetadata, "Current field error.", Field: "taxes")] };
        var reused = await progress.FindSavedAsync(metadata, 2, 8, default);
        Assert.NotNull(reused); Assert.Equal(DocumentAcquisitionSource.Reused, reused.AcquisitionSource);
        Assert.Equal(45.67m, reused.Metadata!.Amount); Assert.Equal(2, reused.PageNumber); Assert.Equal(8, reused.RowIndex);
        Assert.All(reused.Errors, e => { Assert.Equal(2, e.PageNumber); Assert.Equal(8, e.RowIndex); });
        Assert.Equal(1, parser.Calls); Assert.Equal(0, ((MemoryStorage)provider.GetRequiredService<IDocumentStorage>()).Reads);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var rows = await db.Results.Where(x => x.ExtractionId == first.Id || x.ExtractionId == second.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(rows[0].FileId, rows[1].FileId); Assert.Equal(rows[0].ConversionId, rows[1].ConversionId);
        Assert.Equal(DocumentAcquisitionSource.Downloaded, rows[0].AcquisitionSource);
        using var scope = provider.CreateScope(); var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var summary = await jobs.GetAsync(second.Id, query.CompanyId, default);
        Assert.Equal(1, summary!.ReusedCount); Assert.Equal(0, summary.NewlyDownloadedCount);
        Assert.Equal(summary.DownloadedCount, summary.ReusedCount + summary.NewlyDownloadedCount);
        Assert.Equal(summary.DiscoveredCount, summary.DownloadedCount + summary.FailedCount);
        var list = await jobs.DocumentsAsync(second.Id, query.CompanyId, 0, 100, default);
        Assert.Equal("reused", list!.Items.Single().GetProperty("acquisitionSource").GetString());
    }

    [PostgresFact]
    public async Task Reuse_IsolatedByOwnerCompanyDirectionTypeAndFormatAndPreservesOriginalPeriod()
    {
        await using var provider = await Provider(new StubParser());
        var query = Query(); var key = new string('7', 49); var first = await Start(provider, query);
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        foreach (var different in new[] { query with { CompanyId = "another-" + Guid.NewGuid().ToString("N") },
            query with { User = "1719956854001" }, query with { DocumentType = DocumentType.CreditNote }, query with { DownloadFormat = DownloadFormat.Pdf } })
        {
            var run = await Start(provider, different);
            Assert.Null(await Progress(provider, run.Id, run.Attempt, different).FindSavedAsync(key, 1, 0, default));
        }
        var nextPeriod = query with { Month = 10 }; var second = await Start(provider, nextPeriod);
        Assert.NotNull(await Progress(provider, second.Id, second.Attempt, nextPeriod).FindSavedAsync(key, 1, 0, default));
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var document = await db.Documents.SingleAsync(x => x.CompanyId == query.CompanyId);
        var file = await db.Files.SingleAsync(x => x.DocumentId == document.Id);
        Assert.Equal(9, file.Month);
        document.Direction = DocumentDirection.Issued; await db.SaveChangesAsync();
        var third = await Start(provider, query);
        Assert.Null(await Progress(provider, third.Id, third.Attempt, query).FindSavedAsync(key, 1, 0, default));
    }

    [PostgresFact]
    public async Task Refresh_BypassesOtherJobsButRecoveryPreservesAcquisitionAndIdempotency()
    {
        await using var provider = await Provider(new StubParser()); var query = Query(); var key = new string('8', 49);
        var first = await Start(provider, query); await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        var refresh = query with { DownloadPolicy = DownloadPolicy.Refresh }; var second = await Start(provider, refresh);
        var progress = Progress(provider, second.Id, second.Attempt, refresh);
        Assert.Null(await progress.FindSavedAsync(key, 1, 0, default));
        await progress.SaveAsync(Document(key), default);
        Assert.Equal(DocumentAcquisitionSource.Downloaded, (await progress.FindSavedAsync(key, 2, 3, default))!.AcquisitionSource);
        using var scope = provider.CreateScope(); var jobs = scope.ServiceProvider.GetRequiredService<IExtractionJobs>();
        var requestId = Guid.NewGuid(); var accepted = await jobs.AcceptAsync(query, requestId, default);
        Assert.Equal(accepted.ExtractionId, (await jobs.AcceptAsync(refresh, requestId, default)).ExtractionId);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        Assert.Equal(DownloadPolicy.ReuseValid, (await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId)).DownloadPolicy);
        Assert.Single(await db.Results.Where(x => x.ExtractionId == second.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task Inspection_MissingAllowsDownloadButFailureIsExplicitAndChangedRevisionRevalidates()
    {
        var parser = new StubParser(); await using var provider = await Provider(parser); var query = Query(); var key = new string('9', 49);
        var first = await Start(provider, query); await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        var storage = (MemoryStorage)provider.GetRequiredService<IDocumentStorage>();
        var second = await Start(provider, query); storage.InspectionStatus = StorageInspectionStatus.Missing;
        Assert.Null(await Progress(provider, second.Id, second.Attempt, query).FindSavedAsync(key, 1, 0, default));
        storage.InspectionStatus = StorageInspectionStatus.Failed;
        var failed = await Progress(provider, second.Id, second.Attempt, query).FindSavedAsync(key, 1, 0, default);
        Assert.NotNull(failed); Assert.Equal(DocumentDownloadStatus.Failed, failed.DownloadStatus);
        Assert.Equal(DocumentStorageStatus.NotAttempted, failed.StorageStatus);
        Assert.Equal(ExtractionErrorCode.StorageVerificationFailed, failed.Errors.Single().Code);
        storage.InspectionStatus = StorageInspectionStatus.Exists;
        var xml = DocumentValidatorTests.Fixture(); storage.Bytes = Encoding.UTF8.GetBytes(xml.Replace(DocumentValidatorTests.AccessKey(xml), key));
        var third = await Start(provider, query);
        var verified = await Progress(provider, third.Id, third.Attempt, query).FindSavedAsync(key, 1, 0, default);
        Assert.NotNull(verified); Assert.Equal(Convert.ToHexString(SHA256.HashData(storage.Bytes)), verified.Validation.Sha256);
        Assert.Equal(1, storage.Reads); Assert.Equal(2, parser.Calls);
        storage.Bytes = "<html>portal error</html>"u8.ToArray();
        var fourth = await Start(provider, query);
        Assert.Null(await Progress(provider, fourth.Id, fourth.Attempt, query).FindSavedAsync(key, 1, 0, default));
    }

    [PostgresFact]
    public async Task Reuse_ThousandResultsUsesPageCandidatesWithoutReadingFilesOrCallingParserAgain()
    {
        var parser = new StubParser(); await using var provider = await Provider(parser); var query = Query();
        var first = await Start(provider, query); var original = Progress(provider, first.Id, first.Attempt, query);
        var keys = Enumerable.Range(1, 1000).Select(x => x.ToString("D49", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        foreach (var key in keys) await original.SaveAsync(Document(key), default);
        var second = await Start(provider, query); var progress = Progress(provider, second.Id, second.Attempt, query);
        foreach (var page in keys.Chunk(50))
        {
            await progress.PreparePageAsync(page, default);
            foreach (var key in page) Assert.Equal(DocumentAcquisitionSource.Reused, (await progress.FindSavedAsync(key, 1, 0, default))!.AcquisitionSource);
        }
        Assert.Equal(1000, parser.Calls); Assert.Equal(0, ((MemoryStorage)provider.GetRequiredService<IDocumentStorage>()).Reads);
        using var scope = provider.CreateScope(); var summary = await scope.ServiceProvider.GetRequiredService<IExtractionJobs>().GetAsync(second.Id, query.CompanyId, default);
        Assert.Equal(1000, summary!.ReusedCount); Assert.Equal(0, summary.NewlyDownloadedCount);
    }

    [PostgresFact]
    public async Task Reuse_ParserVersionAndValidationProfileChangesReadStoredXmlWithoutSri()
    {
        var parser = new StubParser(); await using var provider = await Provider(parser); var query = Query();
        var xml = DocumentValidatorTests.Fixture(); var key = DocumentValidatorTests.AccessKey(xml);
        var storage = (MemoryStorage)provider.GetRequiredService<IDocumentStorage>(); storage.Bytes = Encoding.UTF8.GetBytes(xml);
        var first = await Start(provider, query); var result = Document(key);
        var evidence = await provider.GetRequiredService<IDocumentValidator>().ValidateAsync(new DocumentContent(new MemoryStream(storage.Bytes), DownloadFormat.Xml), query.DocumentType, key);
        result.Validation = evidence; result.SourceXml = xml; result.XmlHash = evidence.Sha256;
        result.Storage = result.Storage! with { Revision = Convert.ToHexString(SHA256.HashData(storage.Bytes)) };
        await Progress(provider, first.Id, first.Attempt, query).SaveAsync(result, default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var old = await db.Conversions.SingleAsync(x => x.DocumentId == db.Documents.Where(d => d.CompanyId == query.CompanyId).Select(d => d.Id).Single());
        old.ParserVersion = "previous-version"; await db.SaveChangesAsync();
        var second = await Start(provider, query);
        Assert.NotNull(await Progress(provider, second.Id, second.Attempt, query).FindSavedAsync(key, 1, 0, default));
        Assert.Equal(1, storage.Reads); Assert.Equal(2, parser.Calls);
        var file = await db.Files.SingleAsync(x => x.DocumentId == old.DocumentId);
        file.ValidationProfile = "previous-profile"; await db.SaveChangesAsync();
        var third = await Start(provider, query);
        Assert.NotNull(await Progress(provider, third.Id, third.Attempt, query).FindSavedAsync(key, 1, 0, default));
        Assert.Equal(2, storage.Reads); Assert.Equal(2, parser.Calls);
        // Historical snapshots and conversions keep their original evidence.
        await db.Entry(old).ReloadAsync(); Assert.Equal("previous-version", old.ParserVersion);
    }

    [PostgresFact]
    public async Task Reuse_PdfKeepsXmlConversionAndDifferentConfiguredProviderRequiresDownload()
    {
        var parser = new StubParser(); await using var provider = await Provider(parser); var query = Query(); var key = new string('5', 49);
        var xmlRun = await Start(provider, query); await Progress(provider, xmlRun.Id, xmlRun.Attempt, query).SaveAsync(Document(key), default);
        var pdfQuery = query with { DownloadFormat = DownloadFormat.Pdf }; var first = await Start(provider, pdfQuery);
        await Progress(provider, first.Id, first.Attempt, pdfQuery).SaveAsync(Document(key, DownloadFormat.Pdf), default);
        var second = await Start(provider, pdfQuery);
        var pdf = await Progress(provider, second.Id, second.Attempt, pdfQuery).FindSavedAsync(key, 1, 0, default);
        Assert.NotNull(pdf); Assert.Equal(DocumentParseStatus.NotApplicable, pdf.ParseStatus);
        Assert.Equal(1, parser.Calls); Assert.Equal(0, ((MemoryStorage)provider.GetRequiredService<IDocumentStorage>()).Reads);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var file = await db.Files.SingleAsync(x => x.Format == DownloadFormat.Pdf && x.DocumentId == db.Documents.Where(d => d.CompanyId == query.CompanyId).Select(d => d.Id).Single());
        file.StorageJson = ExtractionJobs.Serialize(new DocumentStorageReference(StorageProvider.S3, "previous-bucket", "key.pdf")); await db.SaveChangesAsync();
        var third = await Start(provider, pdfQuery);
        Assert.Null(await Progress(provider, third.Id, third.Attempt, pdfQuery).FindSavedAsync(key, 1, 0, default));
        Assert.Single(await db.Conversions.Where(x => x.DocumentId == file.DocumentId).ToListAsync());
    }

    [PostgresFact]
    public async Task Reuse_PrefersExactPeriodThenNewestKnownStorageDate()
    {
        await using var provider = await Provider(new StubParser()); var query = Query(); var key = new string('4', 49);
        var first = await Start(provider, query); await Progress(provider, first.Id, first.Attempt, query).SaveAsync(Document(key), default);
        var august = query with { Month = 8 }; var latest = await Start(provider, august);
        await Progress(provider, latest.Id, latest.Attempt, august).SaveAsync(Document(key), default);
        await using var db = await provider.GetRequiredService<IDbContextFactory<CrawlerDbContext>>().CreateDbContextAsync();
        var originalFile = await db.Files.SingleAsync(x => x.DocumentId == db.Documents.Where(d => d.CompanyId == query.CompanyId).Select(d => d.Id).Single() && x.Month == 9);
        originalFile.StoredAt = null; await db.SaveChangesAsync();
        var exact = await Start(provider, query);
        await Progress(provider, exact.Id, exact.Attempt, query).FindSavedAsync(key, 1, 0, default);
        Assert.Equal(originalFile.Id, (await db.Results.SingleAsync(x => x.ExtractionId == exact.Id)).FileId);
        var october = query with { Month = 10 }; var other = await Start(provider, october);
        await Progress(provider, other.Id, other.Attempt, october).FindSavedAsync(key, 1, 0, default);
        var chosen = await db.Files.SingleAsync(x => x.Id == db.Results.Where(r => r.ExtractionId == other.Id).Select(r => r.FileId).Single());
        Assert.Equal(8, chosen.Month);
    }

    private sealed class StubParser : ISriDocumentJsonParser
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Task<JsonConversion> ParseAsync(string xml, DocumentType type, string key, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(Fail ? new JsonConversion(DocumentParseStatus.Failed, ErrorCode: "conversionFailed")
                : new JsonConversion(DocumentParseStatus.Parsed, JsonSerializer.SerializeToElement(new { infoTributaria = new { ruc = "1719956854001", claveAcceso = key } }), HttpSriDocumentJsonParser.ParserName, HttpSriDocumentJsonParser.ParserVersion));
        }
    }
    private sealed class MemoryStorage : IDocumentStorage
    {
        public bool CanRead(DocumentStorageReference reference) => reference.Provider == StorageProvider.Local;
        public StorageInspectionStatus InspectionStatus { get; set; } = StorageInspectionStatus.Exists;
        public int Inspections { get; private set; }
        public Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
        {
            Inspections++;
            var revision = Bytes.SequenceEqual("fixture"u8.ToArray()) ? "fixture-revision" : Convert.ToHexString(SHA256.HashData(Bytes));
            return Task.FromResult(new DocumentStorageInspection(InspectionStatus, Bytes.Length, revision));
        }
        public byte[] Bytes { get; set; } = Encoding.UTF8.GetBytes("fixture");
        public int Reads { get; private set; }
        public Task<Models.Storage.DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Open());
        private Stream Open() { Reads++; return new MemoryStream(Bytes); }
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
