using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace DescagaCompronanteSRI.Tests.Storage;

public class StorageIntegrationTests(ITestOutputHelper output)
{
    [StorageFact]
    public async Task RemoteStorage_RoundTripsSyntheticXmlAndPdfAndCleansUp()
    {
        var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DescagaCompronanteSRI"))
        };
        var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        StorageConfiguration.AddStorageEnvironmentFile(configuration, environment);
        var settings = new DocumentStorageOptions();
        settings.Load(configuration);
        Assert.NotEqual(StorageProvider.Local, settings.Provider);
        using var client = StorageConfiguration.CreateClient(settings);
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(settings));
        var context = StorageTestSupport.Context with
        {
            CompanyId = "storage-test-" + Guid.NewGuid().ToString("N"),
            TaxpayerId = "0000000000000", AccessKey = new string('0', 49)
        };
        var attemptedKeys = new List<string>();
        var cleanupFailures = new List<string>();
        try
        {
            foreach (var format in new[] { DownloadFormat.Xml, DownloadFormat.Pdf })
            {
                var bytes = Encoding.UTF8.GetBytes(format == DownloadFormat.Xml
                    ? "<?xml version=\"1.0\" encoding=\"utf-8\"?><storageTest>synthetic</storageTest>"
                    : "%PDF-1.4\n% Synthetic storage transport fixture, no taxpayer data.\n%%EOF\n");
                attemptedKeys.Add(DocumentStorageKey.Create(context, format));
                await using var content = new DocumentContent(new MemoryStream(bytes), format);
                var reference = await storage.SaveAsync(context, content);
                await using var saved = await storage.OpenReadAsync(reference);
                using var copy = new MemoryStream();
                await saved.CopyToAsync(copy);
                Assert.Equal(bytes, copy.ToArray());
                var metadata = await client.GetObjectMetadataAsync(settings.Bucket, reference.Key);
                Assert.Equal(DocumentStorageKey.ContentType(format), metadata.Headers.ContentType);
                Assert.Equal(bytes.Length, metadata.ContentLength);
                output.WriteLine($"{settings.Provider}: {format} upload/read verified ({bytes.Length} bytes).");
            }
        }
        catch (AmazonS3Exception exception)
        {
            throw new InvalidOperationException($"Storage integration failed: {exception.ErrorCode}, HTTP {(int)exception.StatusCode}.");
        }
        finally
        {
            foreach (var key in attemptedKeys)
            {
                try
                {
                    // Delete the exact test version when bucket versioning is enabled.
                    var metadata = await client.GetObjectMetadataAsync(settings.Bucket, key);
                    await client.DeleteObjectAsync(new DeleteObjectRequest
                    {
                        BucketName = settings.Bucket, Key = key, VersionId = metadata.VersionId
                    });
                }
                catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
                catch (AmazonS3Exception exception) { cleanupFailures.Add($"{exception.ErrorCode} ({(int)exception.StatusCode})"); }
            }
            Assert.True(cleanupFailures.Count == 0,
                $"Cleanup failed for test prefix {context.CompanyId}: {string.Join(", ", cleanupFailures)}");
            output.WriteLine($"Cleaned test objects under {context.CompanyId}/.");
        }
    }
}

public sealed class StorageFactAttribute : FactAttribute
{
    public StorageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SRI_STORAGE_TESTS") != "1")
            Skip = "Set SRI_STORAGE_TESTS=1 to write/read/delete synthetic documents in the configured remote bucket.";
    }
}
