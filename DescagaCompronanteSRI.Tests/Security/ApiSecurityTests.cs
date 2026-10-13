using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DescagaCompronanteSRI.ApiSecurity;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Security;
using DescagaCompronanteSRI.Tests.ReceivedDocuments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Security;

[Collection("HangfireHost")]
public sealed class ApiSecurityTests
{
    internal static object Query(string company = "acme") => new { companyId = company, user = "1790012345001", password = "fixture-only",
        year = 2026, month = 5, day = 0, documentType = "invoice", downloadFormat = "xml" };
    internal static HttpClient Client(SecurityFactory factory, string? key = null)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        if (key is not null) client.DefaultRequestHeaders.Add(ApiAccess.Header, key);
        return client;
    }

    [Fact]
    public void Tokens_AreUniqueCanonicalAndContainOnlyAnIndependentIdentifierAndRandomSecret()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => ApiKeyToken.Create()).ToArray();
        Assert.Equal(100, tokens.Select(x => x.Token).Distinct().Count());
        foreach (var value in tokens)
        {
            Assert.Equal(80, value.Token.Length); Assert.Equal(32, value.Hash.Length);
            Assert.True(ApiKeyToken.TryParse(value.Token, out var id, out var hash));
            Assert.Equal(value.Id, id); Assert.Equal(value.Hash, hash);
            Assert.False(ApiKeyToken.TryParse(value.Token + " ", out _, out _));
            Assert.False(ApiKeyToken.TryParse(value.Token.Replace('.', '/'), out _, out _));
        }
    }

    [Fact]
    public async Task MissingInvalidDuplicateAndAlternativeTransports_DoNotAcceptExtractions()
    {
        await using var factory = new SecurityFactory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/received-documents/query", Query())).StatusCode);
        foreach (var key in new[] { "invalid", factory.Company.Token + " ", factory.Company.Token + "," + factory.Company.Token })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/received-documents/query") { Content = JsonContent.Create(Query()) };
            request.Headers.TryAddWithoutValidation(ApiAccess.Header, key);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
        }
        using var duplicate = new HttpRequestMessage(HttpMethod.Post, "/api/received-documents/query") { Content = JsonContent.Create(Query()) };
        duplicate.Headers.TryAddWithoutValidation(ApiAccess.Header, new[] { factory.Company.Token, factory.Company.Token });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(duplicate)).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", "XApiKey=" + factory.Company.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/received-documents/query?XApiKey=" + factory.Company.Token, Query())).StatusCode);
    }

    [Fact]
    public async Task CompanyCredentialsCannotCreateForeignExtractionsOrReadForeignRoutes()
    {
        await using var factory = new SecurityFactory(); using var client = Client(factory, factory.Company.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/received-documents/query", Query("other"))).StatusCode);
        var response = await client.PostAsJsonAsync("/api/received-documents/query", Query());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<ExtractionAccepted>())!.ExtractionId;
        foreach (var suffix in new[] { "", "/documents", "/documents/1", "/documents/1/file", "/errors", "/attempts" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/received-documents/extractions/{id}{suffix}?companyId=other")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/received-documents/extractions/{id}/attempts?companyId=acme")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/companies/acme/api-keys")).StatusCode);
    }

    [Fact]
    public async Task AdministratorCannotReadOrCreateReceivedDocumentsAndDatabaseFailureDeniesAccess()
    {
        await using var factory = new SecurityFactory(); using var admin = Client(factory, factory.Admin.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/received-documents/query", Query())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/received-documents/extractions/{Guid.NewGuid()}?companyId=acme")).StatusCode);
        factory.Keys.Unavailable = true;
        using var company = Client(factory, factory.Company.Token);
        var response = await company.PostAsJsonAsync("/api/received-documents/query", Query());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("fixture-only", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProtectedModeDoesNotExposeIssuedSwaggerDashboardOrAnyStaticDocument()
    {
        await using var factory = new SecurityFactory(); using var client = Client(factory, factory.Company.Token);
        foreach (var path in new[] { "/swagger/v1/swagger.json", "/swagger/index.html", "/hangfire", "/documents/acme/example.xml", "/emitidos/example.pdf" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/SriEmitidos/consultar", new { })).StatusCode);
        using var anonymous = Client(factory);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/health")).StatusCode);
    }

    [Theory]
    [InlineData("Production", "localDevelopment", true)]
    [InlineData("Development", "required", false)]
    [InlineData("Production", "required", false)]
    [InlineData("Development", "999", true)]
    public void StartupRejectsUnsafeOrIncompleteConfiguration(string environment, string mode, bool configured)
    {
        using var factory = new SecurityFactory(environment, mode, administratorConfigured: configured);
        Assert.Throws<OptionsValidationException>(() => Client(factory));
    }

    [Fact]
    public async Task LocalModeKeepsCompanyDevelopmentUnprotectedButAdminRequiresItsCredentialAndSwaggerIsAccurate()
    {
        await using var factory = new SecurityFactory("Development", "localDevelopment"); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/received-documents/query", Query())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/companies/acme/api-keys")).StatusCode);
        var swagger = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")).RootElement;
        var paths = swagger.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/SriEmitidos/consultar", out _));
        Assert.True(paths.GetProperty("/api/admin/companies/{companyId}/api-keys").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/received-documents/query").GetProperty("post").TryGetProperty("security", out _));
        Assert.Equal("XApiKey", swagger.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/documents/acme/example.xml")).StatusCode);
    }

    [Fact]
    public async Task ControlledLogsDoNotContainCredentialsHashesOrBodies()
    {
        await using var factory = new SecurityFactory(); using var client = Client(factory, factory.Company.Token);
        await client.PostAsJsonAsync("/api/received-documents/query", Query());
        using var invalid = Client(factory, factory.Company.Token + "sensitive-marker");
        await invalid.PostAsJsonAsync("/api/received-documents/query", Query());
        factory.Keys.Unavailable = true; await client.PostAsJsonAsync("/api/received-documents/query", Query());
        var logs = string.Join('\n', factory.Logs.Messages);
        foreach (var value in new[] { factory.Admin.Token, factory.Company.Token, Convert.ToBase64String(factory.Company.Hash), "sensitive-marker", "fixture-only" })
            Assert.DoesNotContain(value, logs);
        Assert.Contains("verification unavailable", logs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequiredModeOpenApiMatchesCompanyAndAdminPoliciesAndBinaryFileContract()
    {
        await using var factory = new SecurityFactory(); using var client = Client(factory, factory.Company.Token);
        var specification = factory.Services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>().GetSwagger("v1");
        Assert.DoesNotContain("/api/SriEmitidos/consultar", specification.Paths.Keys);
        Assert.NotEmpty(specification.Paths["/api/received-documents/query"].Operations[Microsoft.OpenApi.Models.OperationType.Post].Security);
        var administration = specification.Paths["/api/admin/companies/{companyId}/api-keys"].Operations[Microsoft.OpenApi.Models.OperationType.Post];
        Assert.NotEmpty(administration.Security); Assert.True(administration.Responses.ContainsKey("401"));
        var download = specification.Paths["/api/received-documents/extractions/{id}/documents/{documentId}/file"].Operations[Microsoft.OpenApi.Models.OperationType.Get];
        Assert.Equal("binary", download.Responses["200"].Content["application/pdf"].Schema.Format);
        Assert.Equal("string", specification.Components.Schemas["CompanyApiKeySummary"].Properties["status"].Type);
    }

    internal sealed class SecurityFactory(string environment = "Production", string mode = "required", bool realDatabase = false,
        bool administratorConfigured = true) : WebApplicationFactory<Program>
    {
        public (Guid Id, string Token, byte[] Hash) Admin { get; } = ApiKeyToken.Create();
        public (Guid Id, string Token, byte[] Hash) Company { get; } = ApiKeyToken.Create();
        public MemoryKeys Keys { get; } = new();
        public CaptureLogs Logs { get; } = new();
        private readonly string root = Path.Combine(Path.GetTempPath(), "sri-security-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "documents", "acme"));
            Directory.CreateDirectory(Path.Combine(root, "wwwroot", "emitidos"));
            File.WriteAllText(Path.Combine(root, "wwwroot", "documents", "acme", "example.xml"), "private-file");
            File.WriteAllText(Path.Combine(root, "wwwroot", "emitidos", "example.pdf"), "private-issued-file");
            builder.UseEnvironment(environment).UseContentRoot(root).UseWebRoot(Path.Combine(root, "wwwroot"));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiSecurity:Mode"] = mode,
                ["ApiSecurity:AdministratorId"] = administratorConfigured ? Admin.Id.ToString() : Guid.Empty.ToString(),
                ["ApiSecurity:AdministratorHash"] = administratorConfigured ? Convert.ToBase64String(Admin.Hash) : "",
                ["DOCUMENT_STORAGE_PROVIDER"] = "Local", ["ExtractionJobs:ConnectionString"] = realDatabase
                    ? Environment.GetEnvironmentVariable("SRI_TEST_POSTGRES") : "Host=127.0.0.1;Port=1;Timeout=1;Database=unavailable",
                ["ExtractionJobs:EncryptionKey"] = Convert.ToBase64String(new byte[32]), ["ExtractionJobs:ParserUrl"] = "http://127.0.0.1:1",
                ["ExtractionJobs:SyncWaitSeconds"] = "1"
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureServices(services =>
            {
                if (!realDatabase)
                {
                    Keys.Companies[Company.Id] = (Company.Hash, "acme");
                    services.RemoveAll<ICompanyApiKeys>(); services.AddSingleton<ICompanyApiKeys>(Keys);
                    services.RemoveAll<IExtractionJobs>(); services.AddSingleton<IExtractionJobs>(new ReceivedDocumentsApiTests.StubJobs(new()));
                }
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    internal sealed class MemoryKeys : ICompanyApiKeys
    {
        public Dictionary<Guid, (byte[] Hash, string Company)> Companies { get; } = [];
        public bool Unavailable { get; set; }
        public Task<ApiKeyIdentity?> AuthenticateAsync(Guid id, byte[] hash, CancellationToken token)
        {
            if (Unavailable) throw new ApiKeyPersistenceUnavailableException();
            return Task.FromResult(Companies.TryGetValue(id, out var key) && CryptographicOperations.FixedTimeEquals(hash, key.Hash)
                ? new ApiKeyIdentity(id, key.Company) : null);
        }
        public Task<CreatedCompanyApiKey> CreateAsync(string companyId, string name, DateTimeOffset? expiresAt, Guid administratorId, CancellationToken token) => throw new NotSupportedException();
        public Task<CursorPage<CompanyApiKeySummary>> ListAsync(string companyId, long cursor, int limit, CancellationToken token) => Task.FromResult(new CursorPage<CompanyApiKeySummary>([], null));
        public Task<bool> RevokeAsync(string companyId, Guid keyId, Guid administratorId, CancellationToken token) => throw new NotSupportedException();
    }
    internal sealed class CaptureLogs : ILoggerProvider
    {
        public System.Collections.Concurrent.ConcurrentBag<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void Dispose() { }
        private sealed class CaptureLogger(CaptureLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => owner.Messages.Add(formatter(state, exception));
        }
    }
}
