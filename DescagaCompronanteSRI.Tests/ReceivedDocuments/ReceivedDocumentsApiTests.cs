using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

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
        {"user":"1790012345001","password":"test-only","year":2026,"month":6,"day":0,"documentType":"invoice","downloadFormat":"xml"}
        """;

    [Fact]
    public async Task Query_AcceptsEnglishContractWithoutAuthentication()
    {
        _factory.Received.QuerySucceeded = true;
        var response = await Post(ValidJson);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("1790012345001", body.GetProperty("taxpayerId").GetString());
        Assert.Equal("completed", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("downloadedCount").GetInt32());
        var document = body.GetProperty("documents")[0];
        Assert.Equal("xml", document.GetProperty("downloadFormat").GetString());
        Assert.Equal("downloaded", document.GetProperty("downloadStatus").GetString());
        Assert.Equal("parsed", document.GetProperty("parseStatus").GetString());
        Assert.False(body.TryGetProperty("querySucceeded", out _));
        Assert.False(document.TryGetProperty("xmlLinkId", out _));
        Assert.False(document.GetProperty("metadata").TryGetProperty("detailId", out _));
        Assert.Equal(DocumentType.Invoice, _factory.Received.LastQuery!.DocumentType);
        Assert.Equal(DownloadFormat.Xml, _factory.Received.LastQuery.DownloadFormat);
        Assert.Equal(0, _factory.Received.LastQuery.Day);
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
    public async Task Query_RejectsMissingRequiredField(string field)
    {
        var fields = JsonSerializer.Deserialize<Dictionary<string, object>>(ValidJson)!;
        fields.Remove(field);
        await AssertRejected(JsonSerializer.Serialize(fields));
    }

    [Theory]
    [InlineData("documentType")]
    [InlineData("downloadFormat")]
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

    [Fact]
    public async Task SwaggerDocumentsStringEnumsAndNewRoute()
    {
        var spec = JsonDocument.Parse(await _client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;
        var paths = spec.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/received-documents/query", out _));
        Assert.False(paths.TryGetProperty("/api/ConsultaComprobantes/consultar", out _));
        Assert.True(paths.TryGetProperty("/api/SriEmitidos/consultar", out _));
        var properties = spec.GetProperty("components").GetProperty("schemas").GetProperty("ReceivedDocumentsQueryRequest").GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("documentType").GetProperty("type").GetString());
        Assert.Contains("invoice", properties.GetProperty("documentType").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("xml", properties.GetProperty("downloadFormat").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
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
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DescagaCompronanteSRI")));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReceivedDocumentsService>();
                services.AddSingleton<IReceivedDocumentsService>(Received);
                services.RemoveAll<IIssuedDocumentsService>();
                services.AddSingleton<IIssuedDocumentsService, StubIssuedService>();
            });
        }
    }

    public sealed class StubReceivedService : IReceivedDocumentsService
    {
        public bool QuerySucceeded { get; set; } = true;
        public int Calls { get; private set; }
        public ReceivedDocumentsQuery? LastQuery { get; private set; }
        public Task<ReceivedDocumentsResponse> QueryAsync(ReceivedDocumentsQuery query)
        {
            Calls++;
            LastQuery = query;
            var result = new ReceivedDocumentsResponse
            {
                TaxpayerId = query.User, QuerySucceeded = QuerySucceeded,
                Status = QuerySucceeded ? ExtractionStatus.Completed : ExtractionStatus.Failed,
                DiscoveredCount = QuerySucceeded ? 1 : 0
            };
            if (QuerySucceeded)
                result.Documents.Add(new()
                {
                    Metadata = new() { AuthorizationNumber = new string('1', 49) },
                    DownloadFormat = query.DownloadFormat, DownloadStatus = DocumentDownloadStatus.Downloaded,
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
