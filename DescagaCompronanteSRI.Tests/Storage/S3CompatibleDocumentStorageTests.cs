using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Storage;

public class S3CompatibleDocumentStorageTests
{
    [Theory]
    [InlineData("S3", DownloadFormat.Xml, "application/xml")]
    [InlineData("S3", DownloadFormat.Pdf, "application/pdf")]
    [InlineData("R2", DownloadFormat.Xml, "application/xml")]
    [InlineData("R2", DownloadFormat.Pdf, "application/pdf")]
    public async Task SaveAndRead_UseConfiguredBucketAndKeepStreamOwnership(string provider, DownloadFormat format, string contentType)
    {
        using var client = new FakeS3();
        var settings = StorageTestSupport.Options(provider);
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(settings));
        var bytes = "synthetic document bytes"u8.ToArray();
        await using var content = new DocumentContent(new MemoryStream(bytes), format);
        content.Stream.Position = 5;
        using var cancellation = new CancellationTokenSource();
        var reference = await storage.SaveAsync(StorageTestSupport.Context, content, cancellation.Token);
        Assert.Equal(settings.Provider, reference.Provider);
        Assert.Equal("test-bucket", reference.Bucket);
        Assert.Null(reference.LocalPath);
        Assert.Equal(DocumentStorageKey.Create(StorageTestSupport.Context, format), reference.Key);
        Assert.Equal(reference.Key, client.PutRequest!.Key);
        Assert.Equal(contentType, client.PutRequest.ContentType);
        Assert.False(client.PutRequest.AutoCloseStream);
        Assert.True(client.PutRequest.AutoResetStreamPosition);
        Assert.Same(content.Stream, client.PutRequest.InputStream);
        Assert.Equal(bytes, client.Bytes);
        Assert.True(content.Stream.CanRead);
        Assert.Equal(cancellation.Token, client.CancellationToken);
        if (provider == "R2")
        {
            Assert.True(client.PutRequest.DisablePayloadSigning);
            Assert.True(client.PutRequest.DisableDefaultChecksumValidation);
        }
        else
        {
            Assert.NotEqual(true, client.PutRequest.DisablePayloadSigning);
            Assert.NotEqual(true, client.PutRequest.DisableDefaultChecksumValidation);
        }
        var stream = await storage.OpenReadAsync(reference, cancellation.Token);
        Assert.Equal(reference.Key, client.GetRequest!.Key);
        Assert.Equal(reference.Bucket, client.GetRequest.BucketName);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.True(client.DownloadStream!.CanRead);
        await stream.DisposeAsync();
        Assert.False(client.DownloadStream.CanRead);
    }

    [Fact]
    public async Task Save_DoesNotCloseSourceOrClaimSuccessOnProviderFailure()
    {
        using var client = new FakeS3 { ThrowOnPut = true };
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(StorageTestSupport.Options()));
        await using var content = new DocumentContent(new MemoryStream("test"u8.ToArray()), DownloadFormat.Xml);
        await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.SaveAsync(StorageTestSupport.Context, content));
        Assert.True(content.Stream.CanRead);
        Assert.Equal(1, client.PutCalls);
        client.ThrowOnPut = false;
        client.StatusCode = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(StorageTestSupport.Context, content));
    }

    [Fact]
    public async Task InvalidReferencesAndAccessKeysDoNotReachProvider()
    {
        using var client = new FakeS3();
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(StorageTestSupport.Options()));
        await using var content = new DocumentContent(new MemoryStream("test"u8.ToArray()), DownloadFormat.Xml);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(StorageTestSupport.Context with { AccessKey = "" }, content));
        Assert.Equal(0, client.PutCalls);
        var key = DocumentStorageKey.Create(StorageTestSupport.Context, DownloadFormat.Xml);
        foreach (var reference in new[]
        {
            new DocumentStorageReference(StorageProvider.S3, "other-bucket", key),
            new DocumentStorageReference(StorageProvider.R2, "test-bucket", key),
            new DocumentStorageReference(StorageProvider.S3, "test-bucket", "../secret")
        })
            await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(reference));
        Assert.Null(client.GetRequest);
    }

    [Fact]
    public async Task FailedReadDisposesResponse()
    {
        using var client = new FakeS3 { StatusCode = HttpStatusCode.InternalServerError };
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(StorageTestSupport.Options()));
        var reference = new DocumentStorageReference(StorageProvider.S3, "test-bucket", DocumentStorageKey.Create(StorageTestSupport.Context, DownloadFormat.Xml));
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(reference));
        Assert.False(client.DownloadStream!.CanRead);
    }

    [Theory]
    [InlineData("S3")]
    [InlineData("R2")]
    public async Task Inspection_UsesHeadWithoutReadingBytesAndPreservesUploadRevision(string provider)
    {
        using var client = new FakeS3(); var settings = StorageTestSupport.Options(provider);
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(settings));
        await using var content = new DocumentContent(new MemoryStream("synthetic"u8.ToArray()), DownloadFormat.Xml);
        var reference = await storage.SaveAsync(StorageTestSupport.Context, content);
        var inspection = await storage.InspectAsync(reference);
        Assert.Equal(StorageInspectionStatus.Exists, inspection.Status);
        Assert.Equal(9, inspection.SizeBytes); Assert.Equal(reference.Revision, inspection.Revision);
        Assert.Equal(reference.Key, client.HeadRequest!.Key); Assert.Equal(reference.Bucket, client.HeadRequest.BucketName);
        Assert.Null(client.GetRequest);
        Assert.False(storage.CanRead(reference with { Bucket = "another" }));
        Assert.False(storage.CanRead(reference with { Provider = StorageProvider.Local }));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StorageInspectionStatus.Missing)]
    [InlineData(HttpStatusCode.Forbidden, StorageInspectionStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, StorageInspectionStatus.Failed)]
    public async Task Inspection_DoesNotTreatAccessFailuresAsMissing(HttpStatusCode status, StorageInspectionStatus expected)
    {
        using var client = new FakeS3 { StatusCode = status };
        var storage = new S3CompatibleDocumentStorage(client, Options.Create(StorageTestSupport.Options()));
        var reference = new DocumentStorageReference(StorageProvider.S3, "test-bucket", DocumentStorageKey.Create(StorageTestSupport.Context, DownloadFormat.Xml));
        Assert.Equal(expected, (await storage.InspectAsync(reference)).Status);
        Assert.Null(client.GetRequest);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.InspectAsync(reference, cancellation.Token));
    }

    private sealed class FakeS3() : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutObjectRequest? PutRequest { get; private set; }
        public GetObjectMetadataRequest? HeadRequest { get; private set; }
        public GetObjectRequest? GetRequest { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }
        public MemoryStream? DownloadStream { get; private set; }
        public bool ThrowOnPut { get; set; }
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int PutCalls { get; private set; }

        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            PutCalls++;
            PutRequest = request;
            CancellationToken = cancellationToken;
            if (ThrowOnPut) throw new AmazonS3Exception("Synthetic provider failure");
            using var copy = new MemoryStream();
            await request.InputStream.CopyToAsync(copy, cancellationToken);
            Bytes = copy.ToArray();
            return new() { HttpStatusCode = StatusCode, ETag = "opaque-provider-revision" };
        }
        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); HeadRequest = request;
            if (StatusCode != HttpStatusCode.OK) throw new AmazonS3Exception("Synthetic HEAD error") { StatusCode = StatusCode };
            return Task.FromResult(new GetObjectMetadataResponse { HttpStatusCode = StatusCode, ContentLength = Bytes.Length, ETag = "opaque-provider-revision" });
        }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            GetRequest = request;
            DownloadStream = new MemoryStream(Bytes);
            return Task.FromResult(new GetObjectResponse { HttpStatusCode = StatusCode, ResponseStream = DownloadStream });
        }
    }
}
