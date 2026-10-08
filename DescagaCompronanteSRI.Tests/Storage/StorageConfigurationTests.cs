using Amazon.Runtime;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Storage;

public class StorageConfigurationTests
{
    [Theory]
    [InlineData("S3", StorageProvider.S3)]
    [InlineData("s3", StorageProvider.S3)]
    [InlineData("R2", StorageProvider.R2)]
    [InlineData("Local", StorageProvider.Local)]
    public void Options_ResolveSupportedProvider(string name, StorageProvider expected)
    {
        var settings = StorageTestSupport.Options(name);
        Assert.Equal(expected, settings.Provider);
    }

    [Fact]
    public void Options_DefaultToLocalAndIgnoreDefaultOrganization()
    {
        var settings = StorageTestSupport.Options(new Dictionary<string, string?> { ["DOCUMENT_STORAGE_DEFAULT_ORGANIZATION_ID"] = "untrusted-default" });
        Assert.Equal(StorageProvider.Local, settings.Provider);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("unknown")]
    [InlineData("S3,R2")]
    public void Options_RejectUnknownProvidersWithoutEchoingValues(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StorageTestSupport.Options(value));
        Assert.Equal("DOCUMENT_STORAGE_PROVIDER must be Local, S3 or R2.", exception.Message);
    }

    [Theory]
    [InlineData("DOCUMENT_STORAGE_BUCKET")]
    [InlineData("AWS_REGION")]
    [InlineData("AWS_ACCESS_KEY_ID")]
    [InlineData("AWS_SECRET_ACCESS_KEY")]
    public void Options_RejectIncompleteS3Configuration(string missing)
    {
        var values = StorageTestSupport.Configuration();
        values.Remove(missing);
        var exception = Assert.Throws<InvalidOperationException>(() => StorageTestSupport.Options(values));
        Assert.Contains(missing, exception.Message);
        Assert.DoesNotContain("test-secret", exception.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://account.r2.cloudflarestorage.com")]
    [InlineData("https://user:password@account.r2.cloudflarestorage.com")]
    [InlineData("https://account.r2.cloudflarestorage.com/bucket")]
    [InlineData("https://account.r2.cloudflarestorage.com?secret=value")]
    public void Options_RejectMissingOrUnsafeR2Endpoint(string endpoint)
    {
        var values = StorageTestSupport.Configuration("R2");
        values["DOCUMENT_STORAGE_SERVICE_URL"] = endpoint;
        var exception = Assert.Throws<InvalidOperationException>(() => StorageTestSupport.Options(values));
        Assert.Contains("DOCUMENT_STORAGE_SERVICE_URL", exception.Message);
    }

    [Fact]
    public void ClientProfiles_UseBoundedRetriesAndProviderSpecificSettings()
    {
        var s3 = StorageConfiguration.CreateClientConfiguration(StorageTestSupport.Options());
        Assert.Equal("us-east-1", s3.RegionEndpoint.SystemName);
        Assert.Equal(2, s3.MaxErrorRetry);
        Assert.Equal(RequestRetryMode.Standard, s3.RetryMode);
        Assert.False(s3.ForcePathStyle);
        Assert.Equal(RequestChecksumCalculation.WHEN_SUPPORTED, s3.RequestChecksumCalculation);
        var values = StorageTestSupport.Configuration("R2");
        values["DOCUMENT_STORAGE_FORCE_PATH_STYLE"] = "true";
        values["AWS_REGION"] = "ignored-for-r2";
        var r2 = StorageConfiguration.CreateClientConfiguration(StorageTestSupport.Options(values));
        Assert.Equal("auto", r2.AuthenticationRegion);
        Assert.StartsWith("https://test-account.r2.cloudflarestorage.com", r2.ServiceURL);
        Assert.True(r2.ForcePathStyle);
        Assert.Equal(2, r2.MaxErrorRetry);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, r2.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, r2.ResponseChecksumValidation);
        values["DOCUMENT_STORAGE_FORCE_PATH_STYLE"] = "invalid";
        Assert.Throws<InvalidOperationException>(() => StorageTestSupport.Options(values));
    }

    [Fact]
    public void EnvironmentFile_DoesNotMutateProcessAndLaterConfigurationWins()
    {
        using var environment = new StorageTestEnvironment();
        var before = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.storage"),
            "DOCUMENT_STORAGE_PROVIDER=S3\nDOCUMENT_STORAGE_BUCKET=parent-bucket\nAWS_SECRET_ACCESS_KEY=synthetic-secret\n");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DOCUMENT_STORAGE_PROVIDER"] = "Local" });
        StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
        Assert.Equal("Local", configuration["DOCUMENT_STORAGE_PROVIDER"]);
        Assert.Equal("parent-bucket", configuration["DOCUMENT_STORAGE_BUCKET"]);
        Assert.Equal("synthetic-secret", configuration["AWS_SECRET_ACCESS_KEY"]);
        Assert.Equal(before, Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));
    }

    [Fact]
    public void EnvironmentFile_PrefersProjectFileThenImmediateParentOnly()
    {
        using var environment = new StorageTestEnvironment();
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.storage"), "DOCUMENT_STORAGE_BUCKET=parent-bucket");
        var project = Path.Combine(environment.ContentRootPath, "project");
        Directory.CreateDirectory(project);
        var originalRoot = environment.ContentRootPath;
        try
        {
            environment.ContentRootPath = project;
            var configuration = new ConfigurationManager();
            StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
            Assert.Equal("parent-bucket", configuration["DOCUMENT_STORAGE_BUCKET"]);
            File.WriteAllText(Path.Combine(project, ".env.storage"), "DOCUMENT_STORAGE_BUCKET=project-bucket");
            configuration = new();
            StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
            Assert.Equal("project-bucket", configuration["DOCUMENT_STORAGE_BUCKET"]);
            environment.ContentRootPath = Path.Combine(project, "nested", "deep");
            configuration = new();
            StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
            Assert.Null(configuration["DOCUMENT_STORAGE_BUCKET"]);
        }
        finally { environment.ContentRootPath = originalRoot; }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public void EnvironmentFile_IsNotLoadedOutsideDevelopment(string name)
    {
        using var environment = new StorageTestEnvironment { EnvironmentName = name };
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.storage"), "DOCUMENT_STORAGE_BUCKET=ignored");
        var configuration = new ConfigurationManager();
        StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
        Assert.Null(configuration["DOCUMENT_STORAGE_BUCKET"]);
    }

    [Fact]
    public void EnvironmentFile_SyntaxErrorDoesNotExposeOriginalLine()
    {
        using var environment = new StorageTestEnvironment();
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.storage"), "AWS_SECRET_ACCESS_KEY=\"synthetic-secret");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StorageConfiguration.AddStorageEnvironmentFile(new ConfigurationManager(), environment));
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
    }

    [Fact]
    public async Task Host_FailsAtStartupForInvalidStorageConfiguration()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DOCUMENT_STORAGE_PROVIDER"] = "invalid-provider" });
        builder.Services.AddDocumentStorage();
        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains("DOCUMENT_STORAGE_PROVIDER", exception.Message);
    }

    [Theory]
    [InlineData("Local", typeof(LocalDocumentStorage))]
    [InlineData("S3", typeof(S3CompatibleDocumentStorage))]
    [InlineData("R2", typeof(S3CompatibleDocumentStorage))]
    public void DependencyInjection_SelectsConfiguredStorage(string name, Type expectedType)
    {
        using var environment = new StorageTestEnvironment();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(StorageTestSupport.Configuration(name)).Build());
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(environment);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
        services.AddLogging();
        services.AddDocumentStorage();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType(expectedType, scope.ServiceProvider.GetRequiredService<IDocumentStorage>());
    }
}
