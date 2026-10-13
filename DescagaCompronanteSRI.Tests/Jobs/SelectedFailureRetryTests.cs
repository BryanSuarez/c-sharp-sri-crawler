using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;
using DescagaCompronanteSRI.Tests.Storage;
using DescagaCompronanteSRI.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Tests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Fixture = DescagaCompronanteSRI.Tests.Jobs.CompanyWorkloadTests.Fixture;

namespace DescagaCompronanteSRI.Tests.Jobs;

[Collection("HangfireHost")]
public sealed class SelectedFailureRetryTests
{
    private static ReceivedDocumentsQuery Query() => new() { CompanyId = "retry-a", User = "1790012345001", Password = "fixture-only",
        Year = 2026, Month = 5, DocumentType = DocumentType.Invoice, DownloadFormat = DownloadFormat.Xml };
    private static ExtractionJobs Jobs(Fixture f, CrawlerDbContext db) => new(db, f.Services.GetRequiredService<CredentialCipher>(), Options.Create(f.Options));
    private static string Key(int i) => i.ToString().PadLeft(49, '0');
    private static async Task<(Guid Id, long[] Rows)> Seed(Fixture f, params DocumentDownloadStatus[] statuses)
    {
        var accepted = await f.Accept(Query());
        await using var db = f.Db(); var parent = await db.Extractions.SingleAsync(x => x.Id == accepted.ExtractionId);
        parent.JobStatus = JobStatus.Completed; parent.EncryptedPassword = null; parent.FinishedAt = DateTimeOffset.UtcNow;
        var rows = statuses.Select((status, i) => new ExtractionDocument { ExtractionId = parent.Id, Identity = Key(i),
            DownloadStatus = status, ResponseJson = ExtractionJobs.Serialize(new ReceivedDocumentResponse { DownloadFormat = DownloadFormat.Xml,
                DownloadStatus = status, Metadata = new() { AuthorizationNumber = Key(i) }, PageNumber = 1, RowIndex = i }) }).ToArray();
        // Results with valid identity have document records, including failed downloads.
        foreach (var row in rows)
        {
            var document = new DocumentRecord { CompanyId = parent.CompanyId, TaxpayerId = parent.TaxpayerId, DocumentType = DocumentType.Invoice,
                Direction = DocumentDirection.Received, AccessKey = row.Identity };
            db.Documents.Add(document); await db.SaveChangesAsync(); row.DocumentId = document.Id;
        }
        db.Results.AddRange(rows); await db.SaveChangesAsync(); return (parent.Id, rows.Select(x => x.Id).ToArray());
    }
    private static async Task<ExtractionAccepted?> Retry(Fixture f, Guid parent, long[] ids, SelectedRetryMode mode = SelectedRetryMode.Download,
        Guid? request = null, string company = "retry-a", string? password = "new-fixture-only")
    { await using var db = f.Db(); return await Jobs(f, db).RetryAsync(parent, company, mode, ids, password, request, default); }

    [PostgresFact]
    public async Task Acceptance_IsAtomicIdempotentAndKeepsSourceUnchanged()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed, DocumentDownloadStatus.Failed, DocumentDownloadStatus.Downloaded);
        var request = Guid.NewGuid();
        var children = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Retry(f, parent.Id, i % 2 == 0 ? parent.Rows[..2] : parent.Rows[..2].Reverse().ToArray(), request: request)));
        var child = children[0]!; Assert.All(children, x => Assert.Equal(child.ExtractionId, x!.ExtractionId));
        await using var db = f.Db(); var record = await db.Extractions.SingleAsync(x => x.Id == child.ExtractionId);
        Assert.Equal(parent.Id, record.SourceExtractionId); Assert.Equal(DownloadPolicy.Refresh, record.DownloadPolicy);
        Assert.DoesNotContain("new-fixture-only", record.QueryJson); Assert.NotNull(record.EncryptedPassword);
        Assert.Equal(2, await db.Extractions.CountAsync()); Assert.Equal(2, await db.Dispatches.CountAsync());
        Assert.Equal(3, await db.Results.CountAsync()); Assert.Null((await db.Extractions.SingleAsync(x => x.Id == parent.Id)).EncryptedPassword);
        var summary = await Jobs(f, db).GetAsync(child.ExtractionId, "retry-a", default);
        Assert.Equal(new SelectedRetryInfo(parent.Id, SelectedRetryMode.Download, 2), summary!.Retry);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => Retry(f, parent.Id, [parent.Rows[0]], request: request));
        Assert.Equal(child.ExtractionId, (await Retry(f, parent.Id, parent.Rows[..2], request: request, password: "another-fixture"))!.ExtractionId);
    }

    [PostgresFact]
    public async Task InvalidSelection_DoesNotCreateWorkOrLeakCompanies()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed, DocumentDownloadStatus.Downloaded);
        Assert.Null(await Retry(f, parent.Id, [parent.Rows[0]], company: "retry-b"));
        foreach (var ids in new[] { Array.Empty<long>(), new[] { parent.Rows[0], parent.Rows[0] }, new[] { -1L }, new[] { 999999L }, new[] { parent.Rows[1] }, Enumerable.Range(1,501).Select(x => (long)x).ToArray() })
            await Assert.ThrowsAsync<RetrySelectionException>(() => Retry(f, parent.Id, ids));
        await Assert.ThrowsAsync<RetrySelectionException>(() => Retry(f, parent.Id, [parent.Rows[0]], password: null));
        await Assert.ThrowsAsync<RetrySelectionException>(() => Retry(f, parent.Id, [parent.Rows[0]], mode: SelectedRetryMode.Parsing));
        await using var db = f.Db(); Assert.Equal(1, await db.Extractions.CountAsync());
        await db.Extractions.Where(x => x.Id == parent.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.JobStatus, JobStatus.Running));
        var busy = await Assert.ThrowsAsync<RetrySelectionException>(() => Retry(f, parent.Id, [parent.Rows[0]]));
        Assert.Equal("retrySourceNotFinished", busy.Code);
    }

    [PostgresFact]
    public async Task Child_UsesLimitsAndCanBeCancelledWithoutChangingTheParent()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed); f.Options.MaxPendingPerCompany = 1;
        var child = (await Retry(f, parent.Id, parent.Rows))!;
        await Assert.ThrowsAsync<WorkloadLimitExceededException>(() => Retry(f, parent.Id, parent.Rows));
        await using var db = f.Db(); await Jobs(f, db).CancelAsync(child.ExtractionId, "retry-a", null, default);
        var runner = new MissingRunner(); await f.Worker(runner).ExecuteAsync(child.ExtractionId, default); Assert.Equal(0, runner.Calls);
        Assert.Equal(JobStatus.Completed, (await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == parent.Id)).JobStatus);
        Assert.NotNull(await Retry(f, parent.Id, parent.Rows));
    }

    [PostgresFact]
    public async Task Worker_ReportsMissingSelectedDocumentsWithoutDownloadingUnselectedOnes()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed, DocumentDownloadStatus.Downloaded);
        var child = (await Retry(f, parent.Id, [parent.Rows[0]]))!;
        var runner = new MissingRunner(); await f.Worker(runner).ExecuteAsync(child.ExtractionId, default);
        Assert.Equal(new[] { Key(0) }, runner.Keys); Assert.Equal(DownloadPolicy.Refresh, runner.Policy);
        await using var db = f.Db(); var status = (await Jobs(f, db).GetAsync(child.ExtractionId, "retry-a", default))!;
        Assert.Equal(JobStatus.Completed, status.JobStatus); Assert.Equal(ExtractionStatus.Failed, status.ExtractionStatus);
        Assert.Equal(1, status.FailedCount); Assert.Equal(0, status.DownloadedCount);
        var row = await db.Results.SingleAsync(x => x.ExtractionId == child.ExtractionId);
        Assert.Contains("selectedDocumentNotFound", row.ResponseJson); Assert.Equal(Key(0), row.Identity);
        Assert.Equal(2, await db.Results.CountAsync(x => x.ExtractionId == parent.Id));
        await f.Worker(runner).ExecuteAsync(child.ExtractionId, default); Assert.Equal(1, runner.Calls);
    }

    [PostgresFact]
    public async Task ParsingRetry_RequiresNoCredentialsDispatchesAndNeverOpensSri()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Downloaded);
        await using (var db = f.Db())
        {
            var row = await db.Results.SingleAsync(); var file = new DocumentFile { DocumentId = row.DocumentId!.Value, Format = DownloadFormat.Xml, Year = 2026, Month = 5 };
            db.Files.Add(file); await db.SaveChangesAsync(); row.FileId = file.Id; row.JsonStatus = DocumentParseStatus.Failed; await db.SaveChangesAsync();
        }
        var child = (await Retry(f, parent.Id, parent.Rows, SelectedRetryMode.Parsing, password: null))!;
        await f.Dispatch(); Assert.Contains(child.ExtractionId, f.Queue.Ids);
        var runner = new MissingRunner(); await f.Worker(runner).ExecuteAsync(child.ExtractionId, default); Assert.Equal(0, runner.Calls);
        await using var final = f.Db(); var record = await final.Extractions.SingleAsync(x => x.Id == child.ExtractionId);
        Assert.Null(record.EncryptedPassword); Assert.Equal(JobStatus.Completed, record.JobStatus); Assert.Equal(ExtractionStatus.Failed, record.ExtractionStatus);
        Assert.Equal(PaginationStatus.NotStarted, ExtractionJobs.Deserialize<PaginationProgress>(record.PaginationJson).Status);
        Assert.Contains("storedRetryFileUnavailable", (await final.Results.SingleAsync(x => x.ExtractionId == child.ExtractionId)).ResponseJson);
    }

    [PostgresFact]
    public async Task Http_RejectsInvalidModesAndEnforcesCompanyRoles()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed);
        await using var original = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        await using var app = original.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExtractionJobs:ConnectionString"] = f.Connection })));
        using var admin = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") }); admin.DefaultRequestHeaders.Add("XApiKey", original.Admin.Token);
        using var anonymous = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var a = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var b = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        foreach (var pair in new[] { (a, "retry-a"), (b, "retry-b") })
        {
            using var created = await admin.PostAsJsonAsync($"/api/admin/companies/{pair.Item2}/api-keys", new { name = "Retry test" });
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); pair.Item1.DefaultRequestHeaders.Add("XApiKey", json.RootElement.GetProperty("apiKey").GetString());
        }
        var path = $"/api/received-documents/extractions/{parent.Id}/retries?companyId=retry-a";
        var body = new { mode = "download", documentIds = parent.Rows, password = "fixture-only" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync(path.Replace("retry-a", "retry-b"), body)).StatusCode);
        foreach (var mode in new string?[] { null, "unknown" })
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(path, new { mode, documentIds = parent.Rows, password = "fixture-only" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(path, new { documentIds = parent.Rows })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await a.PostAsJsonAsync(path, body)).StatusCode);
    }

    [PostgresFact]
    public async Task ParsingStoredXml_PreservesBytesAndHistoryAndReusesConfirmedConversion()
    {
        await using var f = await Fixture.CreateAsync(); using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        var parent = await Seed(f, DocumentDownloadStatus.Downloaded);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", "factura_V1.1.0.xml"));
        await using var content = new DocumentContent(new MemoryStream(bytes), DownloadFormat.Xml);
        var reference = await storage.SaveAsync(new("retry-a", Query().User, 2026, 5, DocumentDirection.Received, DocumentType.Invoice, Key(0)), content);
        long oldConversion;
        string originalResponse;
        await using (var db = f.Db())
        {
            var row = await db.Results.SingleAsync(); originalResponse = row.ResponseJson;
            var file = new DocumentFile { DocumentId = row.DocumentId!.Value, Format = DownloadFormat.Xml, Year = 2026, Month = 5,
                StorageJson = ExtractionJobs.Serialize(reference), LocalPath = reference.LocalPath };
            var conversion = new DocumentConversion { DocumentId = row.DocumentId.Value, Status = DocumentParseStatus.Failed,
                CreatedAt = DateTimeOffset.UtcNow, ErrorCode = "oldFailure" };
            db.Files.Add(file); db.Conversions.Add(conversion); await db.SaveChangesAsync();
            oldConversion = conversion.Id; row.FileId = file.Id; row.ConversionId = conversion.Id; row.JsonStatus = DocumentParseStatus.Failed; await db.SaveChangesAsync();
        }
        var parser = new SwitchingParser { Fail = true }; var runner = new MissingRunner();
        ExtractionWorker Worker() => new(f.Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            f.Services.GetRequiredService<CredentialCipher>(), parser, storage, f.Queue, Options.Create(f.Options),
            NullLogger<ExtractionWorker>.Instance, f.Services.GetRequiredService<IDocumentValidator>(), f.Services.GetRequiredService<IOptions<DocumentValidationOptions>>());
        var first = (await Retry(f, parent.Id, parent.Rows, SelectedRetryMode.Parsing, password: null))!;
        await Worker().ExecuteAsync(first.ExtractionId, default);
        await using (var db = f.Db())
        {
            var status = (await Jobs(f, db).GetAsync(first.ExtractionId, "retry-a", default))!;
            Assert.Equal(ExtractionStatus.Partial, status.ExtractionStatus); Assert.Equal(1, status.ConversionFailedCount);
            Assert.Equal(1, status.ReusedCount); Assert.Equal(0, status.NewlyDownloadedCount);
        }
        parser.Fail = false;
        var second = (await Retry(f, parent.Id, parent.Rows, SelectedRetryMode.Parsing, password: null))!;
        await Worker().ExecuteAsync(second.ExtractionId, default);
        Assert.Equal(2, parser.Calls); Assert.Equal(0, runner.Calls);
        // A later retry of the old failed result can use the now-compatible conversion without inserting a duplicate.
        var third = (await Retry(f, parent.Id, parent.Rows, SelectedRetryMode.Parsing, password: null))!;
        await Worker().ExecuteAsync(third.ExtractionId, default); Assert.Equal(2, parser.Calls);
        await using var final = f.Db();
        Assert.Equal(ExtractionStatus.Completed, (await Jobs(f, final).GetAsync(third.ExtractionId, "retry-a", default))!.ExtractionStatus);
        var source = await final.Results.SingleAsync(x => x.ExtractionId == parent.Id);
        Assert.Equal(originalResponse, source.ResponseJson); Assert.Equal(oldConversion, source.ConversionId);
        Assert.Equal(DocumentParseStatus.Failed, source.JsonStatus);
        Assert.Equal(3, await final.Conversions.CountAsync());
        var success = await final.Results.SingleAsync(x => x.ExtractionId == second.ExtractionId);
        Assert.Equal(success.ConversionId, (await final.Results.SingleAsync(x => x.ExtractionId == third.ExtractionId)).ConversionId);
        await using var stored = await storage.OpenReadAsync(reference); using var actual = new MemoryStream(); await stored.CopyToAsync(actual);
        Assert.Equal(bytes, actual.ToArray()); Assert.Equal(1, await final.Files.CountAsync());
    }

    [PostgresFact]
    public async Task DownloadRetry_RecoversConfirmedFileWithoutAnotherDownload()
    {
        await using var f = await Fixture.CreateAsync(); using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment); var parser = new SwitchingParser();
        var validator = f.Services.GetRequiredService<IDocumentValidator>();
        var parent = await Seed(f, DocumentDownloadStatus.Failed, DocumentDownloadStatus.Downloaded);
        var child = (await Retry(f, parent.Id, [parent.Rows[0]]))!;
        var runner = new RecoveringRunner(storage, validator);
        ExtractionWorker Worker(IReceivedExtractionRunner active) => new(f.Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), active,
            f.Services.GetRequiredService<CredentialCipher>(), parser, storage, f.Queue, Options.Create(f.Options),
            NullLogger<ExtractionWorker>.Instance, validator, f.Services.GetRequiredService<IOptions<DocumentValidationOptions>>());
        await Worker(runner).ExecuteAsync(child.ExtractionId, default);
        await using (var db = f.Db())
        {
            Assert.Equal(JobStatus.Retrying, (await Jobs(f, db).GetAsync(child.ExtractionId, "retry-a", default))!.JobStatus);
            Assert.Single(await db.Results.Where(x => x.ExtractionId == child.ExtractionId).ToListAsync());
        }
        await Worker(runner).ExecuteAsync(child.ExtractionId, default);
        Assert.Equal(1, runner.Downloads); Assert.Equal(1, parser.Calls);
        await using var final = f.Db(); var status = (await Jobs(f, final).GetAsync(child.ExtractionId, "retry-a", default))!;
        Assert.Equal(JobStatus.Completed, status.JobStatus); Assert.Equal(ExtractionStatus.Completed, status.ExtractionStatus);
        Assert.Equal(2, status.Attempts); Assert.Equal(1, status.DownloadedCount);
        Assert.Single(await final.Results.Where(x => x.ExtractionId == child.ExtractionId).ToListAsync());
        Assert.Equal(DocumentDownloadStatus.Failed, (await final.Results.SingleAsync(x => x.Id == parent.Rows[0])).DownloadStatus);
    }

    [PostgresFact]
    public async Task Migration_PreservesHistoricalRowsAndParsingJobsExpireWithoutCredentials()
    {
        await using var f = await Fixture.CreateAsync(); var parent = await Seed(f, DocumentDownloadStatus.Failed);
        await using var db = f.Db();
        await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db)
            .MigrateAsync("20261013035840_ExtractionCancellation");
        await db.Database.MigrateAsync();
        var restored = await db.Extractions.SingleAsync(x => x.Id == parent.Id);
        Assert.Null(restored.SourceExtractionId); Assert.Null(restored.RetryMode); Assert.Null(restored.RetrySelectionJson);
        Assert.Single(await db.Results.ToListAsync());
        var child = (await Retry(f, parent.Id, parent.Rows))!;
        await db.Extractions.Where(x => x.Id == child.ExtractionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RetryMode, SelectedRetryMode.Parsing)
            .SetProperty(x => x.EncryptedPassword, (string?)null).SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await f.Dispatch();
        Assert.Equal(JobStatus.Failed, (await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == child.ExtractionId)).JobStatus);
        Assert.DoesNotContain(child.ExtractionId, f.Queue.Ids);
    }

    [Fact]
    public async Task RetryPersistenceFailure_ReturnsControlled503WithoutSensitiveLogs()
    {
        await using var original = new ApiSecurityTests.SecurityFactory();
        await using var app = original.WithWebHostBuilder(b => b.ConfigureServices(services =>
        { Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<IExtractionJobs>(services); services.AddScoped<IExtractionJobs, ExtractionJobs>(); }));
        using var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("XApiKey", original.Company.Token);
        using var response = await client.PostAsJsonAsync($"/api/received-documents/extractions/{Guid.NewGuid()}/retries?companyId=acme",
            new { mode = "download", documentIds = new[] { 1 }, password = "sensitive-retry-fixture-only" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var logs = string.Join('\n', original.Logs.Messages);
        Assert.DoesNotContain("sensitive-retry-fixture-only", logs); Assert.DoesNotContain(original.Company.Token, logs);
        Assert.DoesNotContain("Host=127.0.0.1", logs);
    }

    private sealed class RecoveringRunner(IDocumentStorage storage, IDocumentValidator validator) : IReceivedExtractionRunner
    {
        public int Downloads;
        public async Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            Assert.Equal(new[] { Key(0) }, query.SelectedAccessKeys!.ToArray());
            await progress.PreparePageAsync([Key(0)], token);
            var saved = await progress.FindSavedAsync(Key(0), 1, 0, token);
            if (saved is null)
            {
                Downloads++;
                var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", "factura_V1.1.0.xml"), token);
                await using var content = new DocumentContent(new MemoryStream(bytes), DownloadFormat.Xml);
                var validation = await validator.ValidateAsync(content, query.DocumentType, Key(0), token);
                Assert.Equal(DocumentValidationStatus.Valid, validation.Status);
                var reference = await storage.SaveAsync(new(query.CompanyId, query.User, query.Year, query.Month, DocumentDirection.Received, query.DocumentType, Key(0)), content, token);
                await progress.SaveAsync(new() { DownloadFormat = DownloadFormat.Xml, Metadata = new() { AuthorizationNumber = Key(0) },
                    DownloadStatus = DocumentDownloadStatus.Downloaded, StorageStatus = DocumentStorageStatus.Stored, Storage = reference,
                    Validation = validation, XmlHash = validation.Sha256, SourceXml = System.Text.Encoding.UTF8.GetString(bytes),
                    ParseStatus = DocumentParseStatus.Parsed, ParsedDocument = new { fixture = true } }, token);
                throw new IOException("Synthetic interruption after confirmed persistence.");
            }
            var response = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true, Status = ExtractionStatus.Completed };
            response.Pagination.Status = PaginationStatus.Completed; return response;
        }
    }

    [PostgresFact]
    public async Task RealHangfire_DispatchesChildAndKeepsParentTerminal()
    {
        await using var f = await Fixture.CreateAsync(realHangfire: true);
        var parent = await Seed(f, DocumentDownloadStatus.Failed);
        var child = (await Retry(f, parent.Id, parent.Rows))!;
        var runner = new MissingRunner();
        var worker = new ExtractionWorker(f.Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            f.Services.GetRequiredService<CredentialCipher>(), f.Services.GetRequiredService<ISriDocumentJsonParser>(),
            f.Services.GetRequiredService<IDocumentStorage>(), f.Services.GetRequiredService<Hangfire.IBackgroundJobClient>(), Options.Create(f.Options),
            NullLogger<ExtractionWorker>.Instance, f.Services.GetRequiredService<IDocumentValidator>(), f.Services.GetRequiredService<IOptions<DocumentValidationOptions>>());
        using var server = new Hangfire.BackgroundJobServer(new Hangfire.BackgroundJobServerOptions { WorkerCount = 1,
            Activator = new RetryActivator(worker), ShutdownTimeout = TimeSpan.FromSeconds(10) }, f.Services.GetRequiredService<Hangfire.JobStorage>());
        await f.Dispatch();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var db = f.Db();
            if (await db.Extractions.AnyAsync(x => x.Id == child.ExtractionId && x.JobStatus == JobStatus.Completed, deadline.Token)) break;
            await Task.Delay(50, deadline.Token);
        }
        await using var final = f.Db();
        Assert.Equal(1, runner.Calls); Assert.Single(await final.Attempts.Where(x => x.ExtractionId == child.ExtractionId).ToListAsync());
        Assert.Equal(JobStatus.Completed, (await final.Extractions.SingleAsync(x => x.Id == parent.Id)).JobStatus);
        Assert.NotNull((await final.Dispatches.SingleAsync(x => x.ExtractionId == child.ExtractionId)).HangfireJobId);
    }
    private sealed class RetryActivator(ExtractionWorker worker) : Hangfire.JobActivator
    { public override object ActivateJob(Type jobType) => jobType == typeof(ExtractionWorker) ? worker : throw new InvalidOperationException(); }

    [PostgresFact]
    public async Task ParsingRetry_CancellationStopsTheParserWithoutLateResultCommits()
    {
        await using var f = await Fixture.CreateAsync(); f.Options.CancellationPollIntervalSeconds = 1;
        var parent = await Seed(f, DocumentDownloadStatus.Downloaded);
        await using (var db = f.Db())
        {
            var row = await db.Results.SingleAsync();
            var file = new DocumentFile { DocumentId = row.DocumentId!.Value, Format = DownloadFormat.Xml, Year = 2026, Month = 5 };
            db.Files.Add(file); await db.SaveChangesAsync(); row.FileId = file.Id; row.JsonStatus = DocumentParseStatus.Failed; await db.SaveChangesAsync();
        }
        var child = (await Retry(f, parent.Id, parent.Rows, SelectedRetryMode.Parsing, password: null))!;
        var parser = new BlockingParser(); var runner = new MissingRunner();
        var worker = new ExtractionWorker(f.Services.GetRequiredService<IDbContextFactory<CrawlerDbContext>>(), runner,
            f.Services.GetRequiredService<CredentialCipher>(), parser, f.Services.GetRequiredService<IDocumentStorage>(), f.Queue,
            Options.Create(f.Options), NullLogger<ExtractionWorker>.Instance, f.Services.GetRequiredService<IDocumentValidator>(),
            f.Services.GetRequiredService<IOptions<DocumentValidationOptions>>(), new VerifiedFileResolver());
        var running = worker.ExecuteAsync(child.ExtractionId, default);
        await parser.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var db = f.Db()) Assert.Equal(JobStatus.CancellationRequested,
            (await Jobs(f, db).CancelAsync(child.ExtractionId, "retry-a", null, default))!.JobStatus);
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        await using var final = f.Db();
        Assert.Equal(JobStatus.Cancelled, (await Jobs(f, final).GetAsync(child.ExtractionId, "retry-a", default))!.JobStatus);
        Assert.Equal(0, runner.Calls); Assert.Empty(await final.Results.Where(x => x.ExtractionId == child.ExtractionId).ToListAsync());
        Assert.Single(await final.Results.Where(x => x.ExtractionId == parent.Id).ToListAsync());
    }
    private sealed class BlockingParser : ISriDocumentJsonParser
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonConversion> ParseAsync(string xml, DocumentType type, string key, CancellationToken token)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException("Unreachable."); }
    }
    private sealed class VerifiedFileResolver : IDocumentReuseResolver
    {
        public Task PreparePageAsync(ReceivedDocumentsQuery query, IReadOnlyList<string> keys, CancellationToken token) => Task.CompletedTask;
        public Task<ReceivedDocumentResponse?> ResolveAsync(ReceivedDocumentsQuery query, ReceivedDocumentMetadata metadata, int page, int row,
            ExtractionDocument? previous, CancellationToken token) => Task.FromResult<ReceivedDocumentResponse?>(new()
            { Metadata = metadata, PageNumber = page, RowIndex = row, DownloadFormat = DownloadFormat.Xml,
                DownloadStatus = DocumentDownloadStatus.Downloaded, StorageStatus = DocumentStorageStatus.Stored,
                Storage = new(StorageProvider.Local, null, "fixture.xml"), SourceXml = "synthetic-fixture", XmlHash = "fixture-hash",
                Validation = new() { Status = DocumentValidationStatus.Valid, Sha256 = "fixture-hash" } });
    }

    private sealed class SwitchingParser : ISriDocumentJsonParser
    {
        public bool Fail; public int Calls;
        public Task<JsonConversion> ParseAsync(string xml, DocumentType type, string key, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(Fail ? new JsonConversion(DocumentParseStatus.Failed, ErrorCode: "parserUnavailable") :
                new JsonConversion(DocumentParseStatus.Parsed, JsonSerializer.SerializeToElement(new { infoTributaria = new { ruc = "0000000000001", claveAcceso = key } }),
                    HttpSriDocumentJsonParser.ParserName, HttpSriDocumentJsonParser.ParserVersion));
        }
    }

    private sealed class MissingRunner : IReceivedExtractionRunner
    {
        public int Calls; public string[] Keys = []; public DownloadPolicy Policy;
        public Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token)
        {
            Calls++; Keys = query.SelectedAccessKeys!.ToArray(); Policy = query.DownloadPolicy;
            var response = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = true, AccumulateDocuments = false };
            response.Pagination.Status = PaginationStatus.Completed; return Task.FromResult(response);
        }
    }
}
