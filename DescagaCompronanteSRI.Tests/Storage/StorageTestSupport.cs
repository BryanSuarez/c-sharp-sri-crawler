using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace DescagaCompronanteSRI.Tests.Storage;

internal static class StorageTestSupport
{
    public static DocumentStorageContext Context => new("acme", "1790012345001", 2026, 6,
        DocumentDirection.Received, DocumentType.Invoice, new string('1', 49));

    public static Dictionary<string, string?> Configuration(string provider = "S3") => new()
    {
        ["DOCUMENT_STORAGE_PROVIDER"] = provider,
        ["DOCUMENT_STORAGE_BUCKET"] = "test-bucket",
        ["AWS_REGION"] = "us-east-1",
        ["AWS_ACCESS_KEY_ID"] = "test-key",
        ["AWS_SECRET_ACCESS_KEY"] = "test-secret",
        ["DOCUMENT_STORAGE_SERVICE_URL"] = provider == "R2" ? "https://test-account.r2.cloudflarestorage.com" : ""
    };

    public static DocumentStorageOptions Options(string provider = "S3") => Options(Configuration(provider));
    public static DocumentStorageOptions Options(Dictionary<string, string?> values)
    {
        var options = new DocumentStorageOptions();
        options.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return options;
    }
}

internal sealed class StorageTestEnvironment : IWebHostEnvironment, IDisposable
{
    public StorageTestEnvironment()
    {
        ContentRootPath = Path.Combine(Path.GetTempPath(), "sri-storage-" + Guid.NewGuid().ToString("N"));
        WebRootPath = Path.Combine(ContentRootPath, "wwwroot");
        Directory.CreateDirectory(WebRootPath);
    }
    public string WebRootPath { get; set; }
    public string ContentRootPath { get; set; }
    public string ApplicationName { get; set; } = "Tests";
    public string EnvironmentName { get; set; } = "Development";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public void Dispose() => Directory.Delete(ContentRootPath, recursive: true);
}
