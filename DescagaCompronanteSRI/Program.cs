using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Services;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using DescagaCompronanteSRI.Models.Enums;
using Microsoft.OpenApi.Any;
using DescagaCompronanteSRI.Services.Storage;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.GetCurrentDirectory(),
    WebRootPath = "wwwroot"
});

StorageConfiguration.AddStorageEnvironmentFile(builder.Configuration, builder.Environment);

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


builder.Services.AddSingleton<IPlaywrightSessionFactory, PlaywrightSessionFactory>();
builder.Services.AddScoped<ISriLoginService, SriLoginService>();
builder.Services.AddScoped<ISriPortalSessionService, SriPortalSessionService>();
builder.Services.AddScoped<IPdfDownloadService, PdfDownloadService>();

builder.Services.AddScoped<IReceivedDocumentsSessionFactory, ReceivedDocumentsSessionFactory>();
builder.Services.AddScoped<IReceivedDocumentsPage, ReceivedDocumentsPage>();
builder.Services.AddScoped<IDocumentParser, DocumentParser>();
builder.Services.AddScoped<IDocumentDownloadStrategy, XmlDocumentDownloadStrategy>();
builder.Services.AddScoped<IDocumentDownloadStrategy, PdfDocumentDownloadStrategy>();
builder.Services.AddScoped<IDocumentDownloader, DocumentDownloader>();
builder.Services.AddDocumentStorage();
builder.Services.AddScoped<IReceivedDocumentsService, ReceivedDocumentsService>();

builder.Services.AddScoped<IIssuedDocumentsService, ConsultaComprobantesEmitidosService>();



builder.Services.AddSwaggerGen(c =>
{
    c.MapType<StorageProvider>(() => EnumSchema<StorageProvider>());
    c.MapType<DocumentType>(() => EnumSchema<DocumentType>());
    c.MapType<DownloadFormat>(() => EnumSchema<DownloadFormat>());
    c.MapType<ExtractionStatus>(() => EnumSchema<ExtractionStatus>());
    c.MapType<DocumentDownloadStatus>(() => EnumSchema<DocumentDownloadStatus>());
    c.MapType<DocumentParseStatus>(() => EnumSchema<DocumentParseStatus>());
    c.MapType<ExtractionErrorCode>(() => EnumSchema<ExtractionErrorCode>());
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Descarga de Comprobantes", Version = "v1" });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "ApiKey must appear in header",
        Type = SecuritySchemeType.ApiKey,
        Name = "XApiKey",
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });
    var key = new OpenApiSecurityScheme
    {
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "ApiKey"
        },
        In = ParameterLocation.Header
    };
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { key, new List<string>() } });
});

builder.Services.AddHttpClient("sri", c =>
{
    c.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

var fileProvider = new PhysicalFileProvider(
    Path.Combine(builder.Environment.ContentRootPath, "wwwroot")
);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    RequestPath = ""
});

app.UseHttpsRedirection();
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
