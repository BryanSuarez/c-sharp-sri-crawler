using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Validation;
using DescagaCompronanteSRI.Services.Parsing;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Services;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using DescagaCompronanteSRI.Services.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Jobs;

public static class JobRegistration
{
    public static void AddJobEnvironmentFile(ConfigurationManager config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        var local = Path.Combine(environment.ContentRootPath, ".env.jobs");
        var parent = Path.Combine(Path.GetDirectoryName(environment.ContentRootPath) ?? environment.ContentRootPath, ".env.jobs");
        var path = File.Exists(local) ? local : File.Exists(parent) ? parent : null;
        if (path is null) return;
        try
        {
            var values = DotNetEnv.Env.NoEnvVars().Load(path).Where(x => x.Key.StartsWith("ExtractionJobs__", StringComparison.Ordinal) || x.Key.StartsWith("DocumentValidation__", StringComparison.Ordinal))
                .ToDictionary(x => x.Key.Replace("__", ":"), x => (string?)x.Value);
            var index = config.Sources.Select((s, i) => (s, i)).Where(x => x.s is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource)
                .Select(x => x.i).DefaultIfEmpty(-1).Max();
            config.Sources.Insert(index + 1, new MemoryConfigurationSource { InitialData = values });
        }
        catch { throw new InvalidOperationException("Unable to read .env.jobs. Check its syntax and permissions."); }
    }
    public static IServiceCollection AddExtractionJobs(this IServiceCollection services, IConfiguration configuration, bool worker = false)
    {
        services.AddOptions<ExtractionJobOptions>().BindConfiguration("ExtractionJobs").ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ExtractionJobs connection string is required.")
            .Validate(o => CredentialCipher.IsValidKey(o.EncryptionKey), "ExtractionJobs requires a base64-encoded 32-byte encryption key.")
            .Validate(o => Uri.TryCreate(o.ParserUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "Parser URL must be HTTP or HTTPS.")
            .ValidateOnStart();
        services.AddDbContextFactory<CrawlerDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.ConnectionString));
        services.AddSingleton<CredentialCipher>();
        services.AddScoped<IExtractionJobs, ExtractionJobs>();
        services.AddHttpClient<ISriDocumentJsonParser, HttpSriDocumentJsonParser>((provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.ParserUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddOptions<DocumentValidationOptions>().BindConfiguration("DocumentValidation").ValidateDataAnnotations()
            .Validate(o => DocumentValidationOptions.IsTimeZoneValid(o.PortalTimeZone), "DocumentValidation PortalTimeZone is invalid.").ValidateOnStart();
        services.AddSingleton<SriSchemaCatalog>();
        services.AddScoped<ReceivedDocumentMetadataParser>();
        services.AddScoped<IDocumentFormatValidator, XmlDocumentValidator>();
        services.AddScoped<IDocumentFormatValidator, PdfDocumentValidator>();
        services.AddScoped<IDocumentValidator, DocumentValidator>();
        services.AddScoped<DocumentReprocessor>();
        services.AddScoped<IDocumentReuseResolver, DocumentReuseResolver>();
        services.AddHangfire((provider, config) => config.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(provider.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.ConnectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", UseSlidingInvisibilityTimeout = true,
                    InvisibilityTimeout = TimeSpan.FromMinutes(2), QueuePollInterval = TimeSpan.FromSeconds(2),
                    AllowDegradedModeWithoutStorage = false }));
        if (worker)
        {
            services.AddScoped<ExtractionWorker>();
            services.AddHangfireServer((provider, options) =>
            {
                options.WorkerCount = provider.GetRequiredService<IOptions<ExtractionJobOptions>>().Value.WorkerCount;
                options.CancellationCheckInterval = TimeSpan.FromSeconds(5);
                options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            });
            services.AddHostedService<JobDispatcher>();
        }
        return services;
    }
    public static IServiceCollection AddReceivedCrawler(this IServiceCollection services)
    {
        services.AddSingleton<IPlaywrightSessionFactory, PlaywrightSessionFactory>();
        services.AddScoped<ISriLoginService, SriLoginService>();
        services.AddScoped<ISriPortalSessionService, SriPortalSessionService>();
        services.AddScoped<IPdfDownloadService, PdfDownloadService>();
        services.AddScoped<IReceivedDocumentsSessionFactory, ReceivedDocumentsSessionFactory>();
        services.AddOptions<ReceivedDocumentsPaginationOptions>().BindConfiguration("ReceivedDocumentsPagination").ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IReceivedDocumentsPage, ReceivedDocumentsPage>();
        services.AddScoped<IDocumentParser, DocumentParser>();
        services.AddScoped<IDocumentDownloadStrategy, XmlDocumentDownloadStrategy>();
        services.AddScoped<IDocumentDownloadStrategy, PdfDocumentDownloadStrategy>();
        services.AddScoped<IDocumentDownloader, DocumentDownloader>();
        services.AddDocumentStorage();
        services.AddScoped<ReceivedDocumentsService>();
        services.AddScoped<IReceivedDocumentsService>(p => p.GetRequiredService<ReceivedDocumentsService>());
        services.AddScoped<IReceivedExtractionRunner>(p => p.GetRequiredService<ReceivedDocumentsService>());
        return services;
    }
}
