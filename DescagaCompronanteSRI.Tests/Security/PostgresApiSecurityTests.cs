using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Security;
using DescagaCompronanteSRI.Services.Storage;
using DescagaCompronanteSRI.Tests.Jobs;
using DescagaCompronanteSRI.Tests.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace DescagaCompronanteSRI.Tests.Security;

[Collection("HangfireHost")]
public sealed class PostgresApiSecurityTests
{
    private static async Task Migrate(ApiSecurityTests.SecurityFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CrawlerDbContext>().Database.MigrateAsync();
    }
    private static async Task<CreatedCompanyApiKey> Create(HttpClient admin, string company, DateTimeOffset? expiration = null)
    {
        var response = await admin.PostAsJsonAsync($"/api/admin/companies/{company}/api-keys", new { name = "Laravel integration", expiresAt = expiration });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedCompanyApiKey>())!;
    }
    private static string Company() => "access-" + Guid.NewGuid().ToString("N");
    private static async Task<(long ResultId, long FileId, long DocumentId, DocumentStorageReference Reference)> SaveFile(
        IServiceProvider provider, Guid extractionId, IDocumentStorage? storage = null)
    {
        using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var extraction = await db.Extractions.SingleAsync(x => x.Id == extractionId);
        var context = new DocumentStorageContext(extraction.CompanyId, extraction.TaxpayerId, 2026, 5, DocumentDirection.Received, DocumentType.Invoice, new string('1', 49));
        await using var content = new DocumentContent(new MemoryStream("<fixture>private document</fixture>"u8.ToArray()), DownloadFormat.Xml);
        var reference = await (storage ?? scope.ServiceProvider.GetRequiredService<IDocumentStorage>()).SaveAsync(context, content);
        var document = new DocumentRecord { CompanyId = context.CompanyId, TaxpayerId = context.TaxpayerId, Direction = context.Direction,
            DocumentType = context.DocumentType, AccessKey = context.AccessKey };
        db.Documents.Add(document); await db.SaveChangesAsync();
        var file = new DocumentFile { DocumentId = document.Id, Format = DownloadFormat.Xml, Year = 2026, Month = 5,
            StorageJson = ExtractionJobs.Serialize(reference), StorageRevision = reference.Revision, LocalPath = reference.LocalPath };
        db.Files.Add(file); await db.SaveChangesAsync();
        var result = new ExtractionDocument { ExtractionId = extraction.Id, SeenAttemptId = Guid.NewGuid(), Identity = context.AccessKey,
            DocumentId = document.Id, FileId = file.Id, DownloadStatus = DocumentDownloadStatus.Downloaded, StorageStatus = DocumentStorageStatus.Stored,
            ResponseJson = ExtractionJobs.Serialize(new ReceivedDocumentResponse { Storage = reference, DownloadFormat = DownloadFormat.Xml,
                DownloadStatus = DocumentDownloadStatus.Downloaded, Metadata = new() { AuthorizationNumber = context.AccessKey } }) };
        extraction.JobStatus = JobStatus.Completed; extraction.ExtractionStatus = ExtractionStatus.Completed; extraction.QuerySucceeded = true;
        extraction.FinishedAt = DateTimeOffset.UtcNow; extraction.EncryptedPassword = null;
        db.Results.Add(result); await db.SaveChangesAsync();
        return (result.Id, file.Id, document.Id, reference);
    }
    private static async Task<ExtractionAccepted> Accept(HttpClient client, string company)
    {
        var response = await client.PostAsJsonAsync("/api/received-documents/query", ApiSecurityTests.Query(company));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExtractionAccepted>())!;
    }

    [PostgresFact]
    public async Task RealHttpAcceptance_TwoCompaniesSameTaxpayerFilesRotationAndRevocation()
    {
        await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        using var admin = ApiSecurityTests.Client(factory, factory.Admin.Token); await Migrate(factory);
        var companyA = Company(); var companyB = Company();
        var a = await Create(admin, companyA); var b = await Create(admin, companyB);
        Assert.InRange((a.Key.ExpiresAt - a.Key.CreatedAt).TotalDays, 89.999, 90.001);
        using var clientA = ApiSecurityTests.Client(factory, a.ApiKey); using var clientB = ApiSecurityTests.Client(factory, b.ApiKey);
        var acceptedA = await Accept(clientA, companyA); var acceptedB = await Accept(clientB, companyB);
        var savedA = await SaveFile(factory.Services, acceptedA.ExtractionId); var savedB = await SaveFile(factory.Services, acceptedB.ExtractionId);
        var filePath = $"/api/received-documents/extractions/{acceptedA.ExtractionId}/documents/{savedA.ResultId}/file?companyId={companyA}";
        var fileResponse = await clientA.GetAsync(filePath);
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal("application/xml", fileResponse.Content.Headers.ContentType!.MediaType);
        Assert.Equal("<fixture>private document</fixture>", await fileResponse.Content.ReadAsStringAsync());
        Assert.EndsWith(".xml", fileResponse.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync(filePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync(filePath.Replace(companyA, companyB))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync(filePath.Replace(savedA.ResultId.ToString() + "/file", savedB.ResultId + "/file"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync("/documents/" + savedA.Reference.Key)).StatusCode);
        foreach (var suffix in new[] { "", "/documents", "/documents/" + savedA.ResultId, "/errors", "/attempts" })
        {
            var route = $"/api/received-documents/extractions/{acceptedA.ExtractionId}{suffix}?companyId={companyA}";
            Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync($"/api/admin/companies/{companyA}/api-keys")).StatusCode);
        var listing = await admin.GetStringAsync($"/api/admin/companies/{companyA}/api-keys");
        Assert.DoesNotContain(a.ApiKey, listing); Assert.DoesNotContain("secretHash", listing); Assert.DoesNotContain("apiKey\"", listing);
        var replacement = await Create(admin, companyA);
        using var rotated = ApiSecurityTests.Client(factory, replacement.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await rotated.GetAsync(filePath)).StatusCode);
        var deletion = $"/api/admin/companies/{companyA}/api-keys/{a.Key.KeyId}";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(deletion)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(deletion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await clientA.GetAsync(filePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rotated.GetAsync(filePath)).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var key = await db.ApiKeys.SingleAsync(x => x.PublicId == a.Key.KeyId);
        Assert.Equal(32, key.SecretHash.Length); Assert.Equal(factory.Admin.Id, key.CreatedBy); Assert.Equal(factory.Admin.Id, key.RevokedBy);
        Assert.Equal(JobStatus.Completed, (await db.Extractions.SingleAsync(x => x.Id == acceptedA.ExtractionId)).JobStatus);
        foreach (var secret in new[] { a.ApiKey, b.ApiKey, replacement.ApiKey, factory.Admin.Token, Convert.ToBase64String(key.SecretHash), "fixture-only", "private document" })
            Assert.DoesNotContain(secret, string.Join('\n', factory.Logs.Messages));
    }

    [PostgresFact]
    public async Task Administration_ValidatesInputsPaginatesExpiresAndDoesNotCancelAlreadyAcceptedJobs()
    {
        await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        using var admin = ApiSecurityTests.Client(factory, factory.Admin.Token); await Migrate(factory); var company = Company();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/companies/ACME S.A./api-keys", new { name = "test" })).StatusCode);
        foreach (var body in new object[] { new { name = " " }, new { name = "test", expiresAt = DateTimeOffset.UtcNow.AddDays(-1) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/companies/{company}/api-keys", body)).StatusCode);
        var first = await Create(admin, company); var second = await Create(admin, company);
        var page = (await admin.GetFromJsonAsync<CursorPage<CompanyApiKeySummary>>($"/api/admin/companies/{company}/api-keys?limit=1"))!;
        Assert.Single(page.Items); Assert.Equal(first.Key.KeyId, page.Items[0].KeyId); Assert.NotNull(page.NextCursor);
        var next = (await admin.GetFromJsonAsync<CursorPage<CompanyApiKeySummary>>($"/api/admin/companies/{company}/api-keys?cursor={page.NextCursor}&limit=500"))!;
        Assert.Single(next.Items); Assert.Equal(second.Key.KeyId, next.Items[0].KeyId); Assert.Null(next.NextCursor);
        foreach (var query in new[] { "cursor=-1", "limit=0", "limit=501" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/admin/companies/{company}/api-keys?{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/companies/{Company()}/api-keys/{first.Key.KeyId}")).StatusCode);
        using var client = ApiSecurityTests.Client(factory, first.ApiKey); var accepted = await Accept(client, company);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        await db.ApiKeys.Where(x => x.PublicId == first.Key.KeyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/received-documents/extractions/{accepted.ExtractionId}?companyId={company}")).StatusCode);
        Assert.Equal(JobStatus.Queued, (await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == accepted.ExtractionId)).JobStatus);
        Assert.NotNull((await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == accepted.ExtractionId)).EncryptedPassword);
        await admin.DeleteAsync($"/api/admin/companies/{company}/api-keys/{first.Key.KeyId}");
        Assert.Equal(JobStatus.Queued, (await db.Extractions.AsNoTracking().SingleAsync(x => x.Id == accepted.ExtractionId)).JobStatus);
    }

    [PostgresFact]
    public async Task FileResolutionRejectsInconsistentOwnershipReferencesAndStorageFailures()
    {
        await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true); using var admin = ApiSecurityTests.Client(factory, factory.Admin.Token);
        await Migrate(factory); var company = Company(); var key = await Create(admin, company); using var client = ApiSecurityTests.Client(factory, key.ApiKey);
        var extraction = await Accept(client, company); var saved = await SaveFile(factory.Services, extraction.ExtractionId);
        var path = $"/api/received-documents/extractions/{extraction.ExtractionId}/documents/{saved.ResultId}/file?companyId={company}";
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CrawlerDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IExtractionDocumentFiles>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.OpenAsync(extraction.ExtractionId, saved.ResultId, company, cancellation.Token));
        await db.Documents.Where(x => x.Id == saved.DocumentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TaxpayerId, "0912345678"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        await db.Documents.Where(x => x.Id == saved.DocumentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TaxpayerId, extraction.TaxpayerId));
        var wrong = ExtractionJobs.Serialize(saved.Reference with { Key = saved.Reference.Key.Replace(company, "other-company") });
        await db.Files.Where(x => x.Id == saved.FileId).ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageJson, wrong));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(path)).StatusCode);
        var restored = ExtractionJobs.Serialize(saved.Reference);
        await db.Files.Where(x => x.Id == saved.FileId).ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageJson, restored));
        File.Delete(saved.Reference.LocalPath!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [PostgresFact]
    public async Task ProtectedFileEndpointStreamsS3AndR2UsingExistingAdaptersAndReleasesTheirResponses()
    {
        foreach (var provider in new[] { "S3", "R2" })
        {
            await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true); using var admin = ApiSecurityTests.Client(factory, factory.Admin.Token);
            await Migrate(factory); var company = Company(); var key = await Create(admin, company); using var client = ApiSecurityTests.Client(factory, key.ApiKey);
            var accepted = await Accept(client, company); using var sdk = new FileS3Client();
            var storage = new S3CompatibleDocumentStorage(sdk, Options.Create(StorageTestSupport.Options(provider)));
            var saved = await SaveFile(factory.Services, accepted.ExtractionId, storage);
            await using var remote = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            { services.RemoveAll<IDocumentStorage>(); services.AddSingleton<IDocumentStorage>(storage); }));
            using var reader = remote.CreateClient(new() { BaseAddress = new Uri("https://localhost") }); reader.DefaultRequestHeaders.Add("XApiKey", key.ApiKey);
            var path = $"/api/received-documents/extractions/{accepted.ExtractionId}/documents/{saved.ResultId}/file?companyId={company}";
            var response = await reader.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(sdk.Bytes, await response.Content.ReadAsByteArrayAsync());
            Assert.False(sdk.Stream!.CanRead); Assert.Equal(saved.Reference.Key, sdk.Request!.Key); Assert.Equal(saved.Reference.Bucket, sdk.Request.BucketName);
            sdk.Failure = HttpStatusCode.NotFound; Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(path)).StatusCode);
            sdk.Failure = HttpStatusCode.Forbidden; Assert.Equal(HttpStatusCode.ServiceUnavailable, (await reader.GetAsync(path)).StatusCode);
        }
    }

    [PostgresFact]
    public async Task ActualDatabaseConnectionFailureReturnsControlled503WithoutLeakingConnectionDetails()
    {
        await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true);
        await using var unavailable = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExtractionJobs:ConnectionString"] = "Host=127.0.0.1;Port=1;Timeout=1;Database=unavailable;Username=test;Password=synthetic-db-credential" })));
        using var client = unavailable.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("XApiKey", factory.Company.Token);
        var response = await client.PostAsJsonAsync("/api/received-documents/query", ApiSecurityTests.Query());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        client.DefaultRequestHeaders.Remove("XApiKey"); client.DefaultRequestHeaders.Add("XApiKey", factory.Admin.Token);
        var adminResponse = await client.GetAsync("/api/admin/companies/acme/api-keys");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, adminResponse.StatusCode);
        var output = await response.Content.ReadAsStringAsync() + await adminResponse.Content.ReadAsStringAsync() + string.Join('\n', factory.Logs.Messages);
        Assert.DoesNotContain("synthetic-db-credential", output); Assert.DoesNotContain(factory.Company.Token, output);
    }

    [PostgresFact]
    public async Task DisconnectDuringStreamingCancelsTheReadAndDisposesTheContent()
    {
        await using var factory = new ApiSecurityTests.SecurityFactory(realDatabase: true); using var admin = ApiSecurityTests.Client(factory, factory.Admin.Token);
        await Migrate(factory); var company = Company(); var key = await Create(admin, company); using var client = ApiSecurityTests.Client(factory, key.ApiKey);
        var accepted = await Accept(client, company); var saved = await SaveFile(factory.Services, accepted.ExtractionId);
        var storage = new BlockingReadStorage();
        await using var streaming = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<IDocumentStorage>(); services.AddSingleton<IDocumentStorage>(storage); }));
        using var reader = streaming.CreateClient(new() { BaseAddress = new Uri("https://localhost") }); reader.DefaultRequestHeaders.Add("XApiKey", key.ApiKey);
        using var cancellation = new CancellationTokenSource();
        var request = reader.GetAsync($"/api/received-documents/extractions/{accepted.ExtractionId}/documents/{saved.ResultId}/file?companyId={company}", cancellation.Token);
        await storage.Content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await storage.Content.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class BlockingReadStorage : IDocumentStorage
    {
        public BlockingReadStream Content { get; } = new();
        public bool CanRead(DocumentStorageReference reference) => reference.Provider == StorageProvider.Local;
        public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(Content);
        public Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class BlockingReadStream() : MemoryStream(new byte[1])
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Closed.TrySetResult(); }
    }

    private sealed class FileS3Client() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public byte[] Bytes { get; set; } = [];
        public MemoryStream? Stream { get; private set; }
        public GetObjectRequest? Request { get; private set; }
        public HttpStatusCode? Failure { get; set; }
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            using var bytes = new MemoryStream(); await request.InputStream.CopyToAsync(bytes, cancellationToken); Bytes = bytes.ToArray();
            return new() { HttpStatusCode = HttpStatusCode.OK, ETag = "fixture-revision" };
        }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Request = request; cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } failure) throw new AmazonS3Exception("Simulated secret external message") { StatusCode = failure };
            Stream = new MemoryStream(Bytes); return Task.FromResult(new GetObjectResponse { HttpStatusCode = HttpStatusCode.OK, ResponseStream = Stream });
        }
    }
}
