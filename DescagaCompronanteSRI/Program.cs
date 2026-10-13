using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Microsoft.OpenApi.Models;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Services;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using DescagaCompronanteSRI.Models.Enums;
using Microsoft.OpenApi.Any;
using DescagaCompronanteSRI.Services.Storage;
using DescagaCompronanteSRI.ApiSecurity;
using DescagaCompronanteSRI.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.GetCurrentDirectory(),
    WebRootPath = "wwwroot"
});

builder.Logging.AddExtractionJsonConsole();

StorageConfiguration.AddStorageEnvironmentFile(builder.Configuration, builder.Environment);
JobRegistration.AddJobEnvironmentFile(builder.Configuration, builder.Environment);
ApiSecurityRegistration.AddSecurityEnvironmentFile(builder.Configuration, builder.Environment);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

builder.Configuration.Sources
    .OfType<Microsoft.Extensions.Configuration.Json.JsonConfigurationSource>()
    .ToList()
    .ForEach(s => s.ReloadOnChange = false);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddCors();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddReceivedCrawler();
builder.Services.AddExtractionJobs(builder.Configuration);
builder.Services.AddApiSecurity(args.Contains("--migrate"));
builder.Services.AddScoped<IExtractionDocumentFiles, ExtractionDocumentFiles>();

builder.Services.AddScoped<IIssuedDocumentsService, ConsultaComprobantesEmitidosService>();



builder.Services.AddSwaggerGen(c =>
{
    c.MapType<ApiKeyState>(() => EnumSchema<ApiKeyState>());
    c.MapType<DiagnosticsCoverage>(() => EnumSchema<DiagnosticsCoverage>());
    c.MapType<DownloadPolicy>(() => EnumSchema<DownloadPolicy>());
    c.MapType<DocumentAcquisitionSource>(() => EnumSchema<DocumentAcquisitionSource>());
    c.MapType<ExecutionMode>(() => EnumSchema<ExecutionMode>());
    c.MapType<JobStatus>(() => EnumSchema<JobStatus>());
    c.MapType<ExtractionStage>(() => EnumSchema<ExtractionStage>());
    c.MapType<StorageProvider>(() => EnumSchema<StorageProvider>());
    c.MapType<DocumentType>(() => EnumSchema<DocumentType>());
    c.MapType<DownloadFormat>(() => EnumSchema<DownloadFormat>());
    c.MapType<PaginationStatus>(() => EnumSchema<PaginationStatus>());
    c.MapType<ExtractionStatus>(() => EnumSchema<ExtractionStatus>());
    c.MapType<DocumentDownloadStatus>(() => EnumSchema<DocumentDownloadStatus>());
    c.MapType<DocumentValidationStatus>(() => EnumSchema<DocumentValidationStatus>());
    c.MapType<DocumentIdentityStatus>(() => EnumSchema<DocumentIdentityStatus>());
    c.MapType<DocumentStorageStatus>(() => EnumSchema<DocumentStorageStatus>());
    c.MapType<MetadataParseStatus>(() => EnumSchema<MetadataParseStatus>());
    c.MapType<DocumentParseStatus>(() => EnumSchema<DocumentParseStatus>());
    c.MapType<ExtractionErrorCode>(() => EnumSchema<ExtractionErrorCode>());
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Descarga de Comprobantes", Version = "v1" });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Server-side credential: administrator for key management, company key for received documents.",
        Type = SecuritySchemeType.ApiKey,
        Name = "XApiKey",
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });
    c.OperationFilter<ApiSecurityOperationFilter>();
});

builder.Services.AddHttpClient("sri", c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CrawlerDbContext>().Database.MigrateAsync();
    return;
}
app.MapGet("/health", async (CrawlerDbContext db, CancellationToken token) =>
    await db.Database.CanConnectAsync(token) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503)).AllowAnonymous();

if (ApiAccess.IsLocal(app.Services.GetRequiredService<IOptions<ApiSecurityOptions>>().Value, app.Environment))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.ConfigObject.PersistAuthorization = false);
    if (app.Services.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.DashboardEnabled)
        app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static OpenApiSchema EnumSchema<T>() where T : struct, Enum => new()
{
    Type = "string",
    Enum = Enum.GetNames<T>().Select(name => (IOpenApiAny)new OpenApiString(
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name))).ToList()
};

public partial class Program { }
