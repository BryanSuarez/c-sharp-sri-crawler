using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Jobs;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

[Collection("HangfireHost")]
public sealed class ReceivedDocumentsApiTests : IClassFixture<ReceivedDocumentsApiTests.ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    public ReceivedDocumentsApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }

    private const string ValidJson = """
        {"executionMode":"sync","companyId":"acme","user":"1790012345001","password":"test-only","year":2026,"month":6,"day":0,"documentType":"invoice","downloadFormat":"xml"}
        """;

    [Fact]
    public async Task Query_AcceptsEnglishContractWithoutAuthentication()
    {
        _factory.Received.QuerySucceeded = true;
        var response = await Post(ValidJson);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("1790012345001", body.GetProperty("taxpayerId").GetString());
        Assert.Equal("acme", body.GetProperty("companyId").GetString());
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("downloadedCount").GetInt32());
        Assert.Equal("completed", body.GetProperty("pagination").GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("pagination").GetProperty("pagesProcessed").GetInt32());
        Assert.Equal(0, body.GetProperty("pagination").GetProperty("duplicateCount").GetInt32());
        var document = body.GetProperty("documents")[0];
        Assert.Equal(1, document.GetProperty("pageNumber").GetInt32());
        Assert.Equal("xml", document.GetProperty("downloadFormat").GetString());
        Assert.Equal("downloaded", document.GetProperty("downloadStatus").GetString());
        Assert.Equal("valid", document.GetProperty("validation").GetProperty("status").GetString());
        Assert.Equal("stored", document.GetProperty("storageStatus").GetString());
        Assert.Equal("partial", document.GetProperty("metadataParseStatus").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("metadata").GetProperty("amount").ValueKind);
        Assert.Equal("2026-06-01", document.GetProperty("metadata").GetProperty("issuedDate").GetString());
        Assert.EndsWith("-05:00", document.GetProperty("metadata").GetProperty("authorizedAtIso").GetString());
        Assert.Equal(1, body.GetProperty("metadataIssueCount").GetInt32());
        Assert.Equal("parsed", document.GetProperty("parseStatus").GetString());
        Assert.Equal("s3", document.GetProperty("storage").GetProperty("provider").GetString());
        Assert.Equal("test-bucket", document.GetProperty("storage").GetProperty("bucket").GetString());
        Assert.False(document.GetProperty("storage").TryGetProperty("localPath", out _));
        Assert.Equal(JsonValueKind.Null, document.GetProperty("filePath").ValueKind);
        Assert.Equal("acme", _factory.Received.LastQuery!.CompanyId);
        Assert.False(body.TryGetProperty("querySucceeded", out _));
        Assert.False(document.TryGetProperty("xmlLinkId", out _));
        Assert.False(document.GetProperty("metadata").TryGetProperty("detailId", out _));
        Assert.Equal(DocumentType.Invoice, _factory.Received.LastQuery!.DocumentType);
        Assert.Equal(DownloadFormat.Xml, _factory.Received.LastQuery.DownloadFormat);
        Assert.Equal(0, _factory.Received.LastQuery.Day);
    }

    [Fact]
    public async Task Query_IncompleteTraversalReturns200WithPartialResultsAndPageLocation()
    {
        _factory.Received.Incomplete = true;
        try
        {
            var response = await Post(ValidJson);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("partial", body.GetProperty("status").GetString());
            Assert.Equal("incomplete", body.GetProperty("pagination").GetProperty("status").GetString());
            Assert.Equal(1, body.GetProperty("discoveredCount").GetInt32());
            Assert.Equal("paginationNavigationFailed", body.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(2, body.GetProperty("errors")[0].GetProperty("pageNumber").GetInt32());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("errors")[0].GetProperty("rowIndex").ValueKind);
        }
        finally { _factory.Received.Incomplete = false; }
    }

    [Fact]
    public async Task Query_ExposesControlledStageFailureAs500()
    {
        _factory.Received.QuerySucceeded = false;
        try
        {
            var response = await Post(ValidJson);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("failed", body.GetProperty("status").GetString());
            Assert.Equal("queryFailed", body.GetProperty("errors")[0].GetProperty("code").GetString());
        }
        finally { _factory.Received.QuerySucceeded = true; }
    }

    [Theory]
    [InlineData("downloadFormat", "unknown")]
    [InlineData("documentType", "unknown")]
    [InlineData("downloadPolicy", "unknown")]
    public async Task Query_RejectsUnknownEnum(string field, string invalidValue)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields[field] = invalidValue;
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Theory]
    [InlineData("downloadFormat")]
    [InlineData("documentType")]
    [InlineData("year")]
    [InlineData("month")]
    [InlineData("day")]
    [InlineData("user")]
    [InlineData("password")]
    [InlineData("companyId")]
    public async Task Query_RejectsMissingRequiredField(string field)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields.Remove(field);
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Theory]
    [InlineData("documentType")]
    [InlineData("downloadFormat")]
    [InlineData("downloadPolicy")]
    public async Task Query_RejectsNumericEnums(string field)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields[field] = 1;
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Fact]
    public async Task Query_RejectsImpossibleDate()
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields["month"] = 2;
        fields["day"] = 30;
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Fact]
    public async Task Query_RejectsLegacyJson()
    {
        await AssertRejected("""
            {"usuario":"1790012345001","password":"test-only","anio":"2026","mes":6,"dia":0,"comprobante":1,"descargarXml":true}
            """);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ACME")]
    [InlineData("../acme")]
    [InlineData("acme/another")]
    [InlineData("acme ")]
    [InlineData("acme\n")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    public async Task Query_RejectsInvalidCompanyBeforeCallingService(string companyId)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields["companyId"] = companyId;
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("bucket")]
    public async Task Query_RejectsStorageConfigurationInBody(string field)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields[field] = "client-controlled";
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Fact]
    public async Task PreviousReceivedRouteIsRemoved()
    {
        var response = await _client.PostAsync("/api/ConsultaComprobantes/consultar", new StringContent(ValidJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IssuedContractIsUnchanged()
    {
        var response = await _client.PostAsJsonAsync("/api/SriEmitidos/consultar", new
        {
            usuario = "1790012345001", password = "test-only", anio = 2026, mes = 6, dia = 1, comprobante = 1
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("ruc", out _));
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.True(body.TryGetProperty("comprobantes", out _));
    }

    [Theory]
    [InlineData("MaxPages", "0")]
    [InlineData("NavigationAttempts", "4")]
    [InlineData("TransitionTimeoutMilliseconds", "60001")]
    [InlineData("RetryDelayMilliseconds", "-1")]
    public void Startup_RejectsInvalidPaginationLimits(string option, string value)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { [$"ReceivedDocumentsPagination:{option}"] = value })));
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task SwaggerDocumentsStringEnumsAndNewRoute()
    {
        var spec = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;
        var paths = spec.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/received-documents/query", out _));
        Assert.True(paths.TryGetProperty("/api/received-documents/extractions/{id}/attempts", out _));
        Assert.False(paths.TryGetProperty("/api/ConsultaComprobantes/consultar", out _));
        Assert.True(paths.TryGetProperty("/api/SriEmitidos/consultar", out _));
        var properties = spec.GetProperty("components").GetProperty("schemas").GetProperty("ReceivedDocumentsQueryRequest").GetProperty("properties");
        Assert.True(properties.TryGetProperty("companyId", out _));
        Assert.False(properties.TryGetProperty("provider", out _));
        Assert.False(properties.TryGetProperty("bucket", out _));
        Assert.Equal("string", properties.GetProperty("documentType").GetProperty("type").GetString());
        Assert.Contains("invoice", properties.GetProperty("documentType").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("xml", properties.GetProperty("downloadFormat").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        var schemas = spec.GetProperty("components").GetProperty("schemas");
        var pagination = schemas.GetProperty("PaginationProgress").GetProperty("properties");
        Assert.Equal("string", pagination.GetProperty("status").GetProperty("type").GetString());
        Assert.Equal(new[] { "notStarted", "completed", "incomplete" },
            pagination.GetProperty("status").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.True(schemas.GetProperty("ReceivedDocumentsResponse").GetProperty("properties").TryGetProperty("pagination", out _));
        Assert.True(schemas.GetProperty("ReceivedDocumentResponse").GetProperty("properties").TryGetProperty("pageNumber", out _));
        Assert.True(schemas.GetProperty("ExtractionError").GetProperty("properties").TryGetProperty("pageNumber", out _));
    }


    [Fact]
    public async Task Query_DefaultsToAsyncAndDoesNotExecuteSri()
    {
        var previous = _factory.Received.Calls;
        var response = await Post(ValidJson.Replace("\"executionMode\":\"sync\",", ""));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("queued", body.GetProperty("jobStatus").GetString());
        Assert.True(body.TryGetProperty("extractionId", out _));
        Assert.Equal(previous, _factory.Received.Calls);
    }
    [Fact]
    public async Task Query_SynchronousTimeoutFallsBackToAcceptedWithoutExecutingInApi()
    {
        _factory.Received.DelayResult = true;
        try { Assert.Equal(HttpStatusCode.Accepted, (await Post(ValidJson)).StatusCode); }
        finally { _factory.Received.DelayResult = false; }
    }
    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("\"unknown\"")]
    public async Task Query_RejectsInvalidExecutionMode(string value) =>
        await AssertRejected(ValidJson.Replace("\"sync\"", value));

    [Theory]
    [InlineData("reuseValid", DownloadPolicy.ReuseValid)]
    [InlineData("refresh", DownloadPolicy.Refresh)]
    public async Task Query_PassesDownloadPolicyToPersistentJob(string value, DownloadPolicy expected)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields["downloadPolicy"] = value;
        Assert.Equal(HttpStatusCode.OK, (await Post(JsonSerializer.Serialize(fields))).StatusCode);
        Assert.Equal(expected, _factory.Received.LastQuery!.DownloadPolicy);
    }
    [Fact]
    public async Task Query_DefaultPolicyIsReuseValidAndNullIsRejected()
    {
        Assert.Equal(HttpStatusCode.OK, (await Post(ValidJson)).StatusCode);
        Assert.Equal(DownloadPolicy.ReuseValid, _factory.Received.LastQuery!.DownloadPolicy);
        await AssertRejected(ValidJson.Replace("{", "{\"downloadPolicy\":null,", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 501)]
    public async Task Attempts_RejectInvalidCursorAndLimit(int cursor, int limit)
    {
        var response = await _client.GetAsync($"/api/received-documents/extractions/{Guid.NewGuid()}/attempts?companyId=acme&cursor={cursor}&limit={limit}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task Attempts_UsesScopedHistoryAndSerializesCoverage()
    {
        var accepted = await _client.PostAsync("/api/received-documents/query", new StringContent(ValidJson.Replace("\"sync\"", "\"async\""), Encoding.UTF8, "application/json"));
        var id = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement.GetProperty("extractionId").GetGuid();
        var path = $"/api/received-documents/extractions/{id}/attempts";
        var response = await _client.GetAsync(path + "?companyId=acme");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("notAvailable", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items")[0].GetProperty("coverage").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path + "?companyId=other")).StatusCode);
    }

    public sealed class StubJobs(StubReceivedService service) : IExtractionJobs
    {
        private readonly Dictionary<Guid, ReceivedDocumentsQuery> queries = [];
        public Task<ExtractionAccepted> AcceptAsync(ReceivedDocumentsQuery query, Guid? requestId, CancellationToken token)
        {
            var id = Guid.NewGuid(); queries[id] = query;
            return Task.FromResult(new ExtractionAccepted(id, query.CompanyId, query.User, JobStatus.Queued, "/status", "/documents", "/errors"));
        }
        public async Task<ReceivedDocumentsResponse?> ResultAsync(Guid id, string company, CancellationToken token) =>
            service.DelayResult ? null : await service.QueryAsync(queries[id]);
        public Task<CursorPage<ExtractionAttemptSummary>?> AttemptsAsync(Guid id, string company, long cursor, int limit, CancellationToken token) =>
            Task.FromResult<CursorPage<ExtractionAttemptSummary>?>(queries.TryGetValue(id, out var query) && query.CompanyId == company
                ? new([new(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, null, null, null, DiagnosticsCoverage.NotAvailable)], null) : null);
        public Task<ExtractionSummary?> GetAsync(Guid id, string company, CancellationToken token) => Task.FromResult<ExtractionSummary?>(null);
        public Task<CursorPage<JsonElement>?> DocumentsAsync(Guid id, string company, long cursor, int limit, CancellationToken token) => Task.FromResult<CursorPage<JsonElement>?>(null);
        public Task<JsonElement?> DocumentAsync(Guid id, string company, long doc, CancellationToken token) => Task.FromResult<JsonElement?>(null);
        public Task<CursorPage<JsonElement>?> ErrorsAsync(Guid id, string company, long cursor, int limit, CancellationToken token) => Task.FromResult<CursorPage<JsonElement>?>(null);
    }

    private Task<HttpResponseMessage> Post(string json) =>
        _client.PostAsync("/api/received-documents/query", new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task AssertRejected(string json)
    {
        var previousCalls = _factory.Received.Calls;
        var response = await Post(json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(previousCalls, _factory.Received.Calls);
    }

    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public StubReceivedService Received { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["DOCUMENT_STORAGE_PROVIDER"] = "Local", ["ExtractionJobs:ConnectionString"] = "Host=localhost;Database=test", ["ExtractionJobs:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", ["ExtractionJobs:SyncWaitSeconds"] = "1" }));
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DescagaCompronanteSRI")));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReceivedDocumentsService>();
                services.AddSingleton<IReceivedDocumentsService>(Received);
                services.RemoveAll<IExtractionJobs>();
                services.AddSingleton<IExtractionJobs>(new StubJobs(Received));
                services.RemoveAll<IIssuedDocumentsService>();
                services.AddSingleton<IIssuedDocumentsService, StubIssuedService>();
            });
        }
    }

    public sealed class StubReceivedService : IReceivedDocumentsService
    {
        public bool QuerySucceeded { get; set; } = true;
        public bool Incomplete { get; set; }
        public bool DelayResult { get; set; }
        public int Calls { get; private set; }
        public ReceivedDocumentsQuery? LastQuery { get; private set; }
        public Task<ReceivedDocumentsResponse> QueryAsync(ReceivedDocumentsQuery query)
        {
            Calls++;
            LastQuery = query;
            var result = new ReceivedDocumentsResponse
            {
                CompanyId = query.CompanyId, TaxpayerId = query.User, QuerySucceeded = QuerySucceeded,
                Status = QuerySucceeded ? ExtractionStatus.Completed : ExtractionStatus.Failed
            };
            result.Pagination.Status = QuerySucceeded ? PaginationStatus.Completed : PaginationStatus.NotStarted;
            result.Pagination.PagesProcessed = QuerySucceeded ? 1 : 0;
            result.Pagination.ReportedTotalCount = QuerySucceeded ? 1 : null;
            if (Incomplete && QuerySucceeded)
            {
                result.Status = ExtractionStatus.Partial;
                result.Pagination.Status = PaginationStatus.Incomplete;
                result.Errors.Add(new(ExtractionErrorCode.PaginationNavigationFailed, "Navigation failed.", PageNumber: 2));
            }
            if (QuerySucceeded)
                result.Documents.Add(new()
                {
                    Metadata = new() { AuthorizationNumber = new string('1', 49), Amount = null, IssuedDate = new DateOnly(2026, 6, 1),
                        AuthorizedAtIso = new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.FromHours(-5)), MetadataParseStatus = MetadataParseStatus.Partial },
                    StorageStatus = DocumentStorageStatus.Stored,
                    Validation = new() { Status = DocumentValidationStatus.Valid, IdentityStatus = DocumentIdentityStatus.Verified, ValidatorVersion = "1", Sha256 = "fixture" },
                    AcquisitionSource = DocumentAcquisitionSource.Downloaded, DownloadFormat = query.DownloadFormat, DownloadStatus = DocumentDownloadStatus.Downloaded,
                    Storage = new DocumentStorageReference(StorageProvider.S3, "test-bucket",
                        "acme/1790012345001/2026/06/received/invoice/" + new string('1', 49) + ".xml"),
                    ParseStatus = DocumentParseStatus.Parsed
                });
            else result.Errors.Add(new(ExtractionErrorCode.QueryFailed, "Query failed."));
            return Task.FromResult(result);
        }
    }

    private sealed class StubIssuedService : IIssuedDocumentsService
    {
        public Task<SriIssuedUserResponse?> QueryAsync(IssuedDocumentsQuery query, string destination) =>
            Task.FromResult<SriIssuedUserResponse?>(new() { Ruc = query.User });
    }
}
