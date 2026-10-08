using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Services.Storage;

public static class StorageConfiguration
{
    public static void AddStorageEnvironmentFile(ConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        var local = Path.Combine(environment.ContentRootPath, ".env.storage");
        var parent = Path.Combine(Path.GetDirectoryName(environment.ContentRootPath) ?? environment.ContentRootPath, ".env.storage");
        var file = File.Exists(local) ? local : File.Exists(parent) ? parent : null;
        if (file is null) return;

        Dictionary<string, string?> values;
        try
        {
            values = DotNetEnv.Env.NoEnvVars().Load(file)
                .Where(pair => pair.Key.StartsWith("DOCUMENT_STORAGE_", StringComparison.Ordinal)
                    || pair.Key is "AWS_REGION" or "AWS_ACCESS_KEY_ID" or "AWS_SECRET_ACCESS_KEY" or "AWS_SESSION_TOKEN")
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (string?)group.Last().Value, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Dotenv parser errors can contain the original line, including credentials.
            throw new InvalidOperationException("Unable to read .env.storage. Check its syntax and file permissions.");
        }

        // Insert after JSON files, before environment variables, command line and test overrides.
        var lastJsonIndex = configuration.Sources.Select((source, index) => (source, index))
            .Where(item => item.source is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource)
            .Select(item => item.index).DefaultIfEmpty(-1).Max();
        configuration.Sources.Insert(lastJsonIndex + 1, new MemoryConfigurationSource { InitialData = values });
    }

    public static IServiceCollection AddDocumentStorage(this IServiceCollection services)
    {
        services.AddOptions<DocumentStorageOptions>()
            .Configure<IConfiguration>((options, configuration) => options.Load(configuration))
            .ValidateOnStart();
        services.AddSingleton<IAmazonS3>(provider => CreateClient(provider.GetRequiredService<IOptions<DocumentStorageOptions>>().Value));
        services.AddScoped<LocalDocumentStorage>();
        services.AddScoped<S3CompatibleDocumentStorage>();
        services.AddScoped<IDocumentStorage>(provider =>
            provider.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.Provider == StorageProvider.Local
                ? provider.GetRequiredService<LocalDocumentStorage>()
                : provider.GetRequiredService<S3CompatibleDocumentStorage>());
        return services;
    }

    public static AmazonS3Config CreateClientConfiguration(DocumentStorageOptions options)
    {
        if (options.Provider == StorageProvider.Local)
            throw new InvalidOperationException("Local storage does not use an S3 client.");
        var config = new AmazonS3Config
        {
            RetryMode = RequestRetryMode.Standard,
            MaxErrorRetry = 2,
            ForcePathStyle = options.ForcePathStyle
        };
        if (options.ServiceUrl is null)
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        else
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }
        if (options.Provider == StorageProvider.R2)
        {
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }
        return config;
    }

    public static IAmazonS3 CreateClient(DocumentStorageOptions options)
    {
        AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
            ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
            : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);
        return new AmazonS3Client(credentials, CreateClientConfiguration(options));
    }
}
