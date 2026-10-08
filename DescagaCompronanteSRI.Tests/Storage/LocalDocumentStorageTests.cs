using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;

namespace DescagaCompronanteSRI.Tests.Storage;

public class LocalDocumentStorageTests
{
    [Fact]
    public async Task WorkerHostWithoutWebEnvironment_CanSaveAndReadDocuments()
    {
        using var directory = new StorageTestEnvironment();
        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = directory.ContentRootPath };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(StorageTestSupport.Configuration("Local")).Build());
        services.AddDocumentStorage();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<DescagaCompronanteSRI.Contracts.IDocumentStorage>();
        await using var content = new DocumentContent(new MemoryStream("worker XML"u8.ToArray()), DownloadFormat.Xml);
        var reference = await storage.SaveAsync(StorageTestSupport.Context, content);
        Assert.StartsWith(Path.Combine(environment.ContentRootPath, "wwwroot", "documents"), reference.LocalPath);
        await using var read = await storage.OpenReadAsync(reference);
        using var reader = new StreamReader(read);
        Assert.Equal("worker XML", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(DownloadFormat.Xml, ".xml")]
    [InlineData(DownloadFormat.Pdf, ".pdf")]
    public async Task SaveAndRead_PreserveBytesAndReturnStableReference(DownloadFormat format, string extension)
    {
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        var bytes = Encoding.UTF8.GetBytes(format == DownloadFormat.Xml ? "<factura/>" : "%PDF test");
        await using var content = new DocumentContent(new MemoryStream(bytes), format);
        content.Stream.Position = 3;
        var reference = await storage.SaveAsync(StorageTestSupport.Context, content);
        Assert.Equal(StorageProvider.Local, reference.Provider);
        Assert.Null(reference.Bucket);
        Assert.Equal($"acme/1790012345001/2026/06/received/invoice/{new string('1', 49)}{extension}", reference.Key);
        Assert.Equal(Path.Combine(environment.WebRootPath, "documents", reference.Key), reference.LocalPath);
        // Reading is based on the validated key, never an untrusted physical path.
        await using var saved = await storage.OpenReadAsync(reference with { LocalPath = "/ignored/path" });
        using var copy = new MemoryStream();
        await saved.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.True(content.Stream.CanRead);
    }

    [Fact]
    public async Task Save_OverwritesOnlyMatchingIdentityAndPreservesLegacyFiles()
    {
        using var environment = new StorageTestEnvironment();
        var legacyDirectory = Path.Combine(environment.WebRootPath, "recibidos", "1790012345001");
        Directory.CreateDirectory(legacyDirectory);
        var legacy = Path.Combine(legacyDirectory, new string('1', 49) + ".xml");
        await File.WriteAllTextAsync(legacy, "legacy");
        var storage = new LocalDocumentStorage(environment);
        await using var first = new DocumentContent(new MemoryStream("original"u8.ToArray()), DownloadFormat.Xml);
        await using var updated = new DocumentContent(new MemoryStream("new"u8.ToArray()), DownloadFormat.Xml);
        var original = await storage.SaveAsync(StorageTestSupport.Context, first);
        var other = await storage.SaveAsync(StorageTestSupport.Context with { CompanyId = "other" }, first);
        var replaced = await storage.SaveAsync(StorageTestSupport.Context, updated);
        Assert.Equal(original.Key, replaced.Key);
        Assert.Equal("new", await File.ReadAllTextAsync(original.LocalPath!));
        Assert.Equal("original", await File.ReadAllTextAsync(other.LocalPath!));
        Assert.Equal("legacy", await File.ReadAllTextAsync(legacy));
    }

    [Fact]
    public async Task Read_RejectsTraversalAndOtherProviders()
    {
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(new(StorageProvider.Local, null, "../secret")));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(new(StorageProvider.S3, "bucket", "any")));
        var reference = new DocumentStorageReference(StorageProvider.Local, null, DocumentStorageKey.Create(StorageTestSupport.Context, DownloadFormat.Xml));
        await Assert.ThrowsAnyAsync<IOException>(() => storage.OpenReadAsync(reference));
    }
}
