using Amazon.S3;
using Amazon.S3.Model;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Services.Storage;

public sealed class S3CompatibleDocumentStorage(IAmazonS3 client, IOptions<DocumentStorageOptions> options) : IDocumentStorage
{
    private readonly DocumentStorageOptions settings = options.Value;

    public bool CanRead(DocumentStorageReference reference) => reference.Provider == settings.Provider && reference.Bucket == settings.Bucket;

    public async Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
    {
        if (!CanRead(reference)) throw new ArgumentException("The storage reference does not belong to the configured provider and bucket.");
        DocumentStorageKey.Validate(reference.Key);
        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = settings.Bucket, Key = reference.Key }, cancellationToken);
            return (int)response.HttpStatusCode is >= 200 and < 300
                ? new(StorageInspectionStatus.Exists, response.ContentLength, response.ETag)
                : new(StorageInspectionStatus.Failed);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return new(StorageInspectionStatus.Missing); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is AmazonS3Exception or HttpRequestException or IOException or OperationCanceledException)
        { return new(StorageInspectionStatus.Failed); }
    }

    public async Task<DocumentStorageReference> SaveAsync(
        DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default)
    {
        var key = DocumentStorageKey.Create(context, content.Format);
        content.Stream.Position = 0;
        var request = new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            InputStream = content.Stream,
            ContentType = DocumentStorageKey.ContentType(content.Format),
            AutoCloseStream = false,
            AutoResetStreamPosition = true
        };
        if (settings.Provider == StorageProvider.R2)
        {
            request.DisablePayloadSigning = true;
            request.DisableDefaultChecksumValidation = true;
        }
        var response = await client.PutObjectAsync(request, cancellationToken);
        if ((int)response.HttpStatusCode is < 200 or >= 300)
            throw new IOException("Document storage did not confirm the upload.");
        return new(settings.Provider, settings.Bucket, key) { Revision = response.ETag };
    }

    public async Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
    {
        if (reference.Provider != settings.Provider || reference.Bucket != settings.Bucket)
            throw new ArgumentException("The storage reference does not belong to the configured provider and bucket.");
        DocumentStorageKey.Validate(reference.Key);
        GetObjectResponse response;
        try
        {
            response = await client.GetObjectAsync(new GetObjectRequest { BucketName = settings.Bucket, Key = reference.Key }, cancellationToken);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        { throw new FileNotFoundException("Stored document was not found."); }
        catch (AmazonS3Exception) { throw new IOException("Stored document could not be read."); }
        if ((int)response.HttpStatusCode is < 200 or >= 300 || response.ResponseStream is null)
        {
            response.Dispose();
            throw new IOException("Document storage did not return the requested content.");
        }
        return new StorageReadStream(response.ResponseStream, response);
    }
}
