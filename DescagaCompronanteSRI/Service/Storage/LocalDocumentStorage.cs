using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;

namespace DescagaCompronanteSRI.Services.Storage;

public sealed class LocalDocumentStorage(IWebHostEnvironment environment) : IDocumentStorage
{
    public async Task<DocumentStorageReference> SaveAsync(
        DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default)
    {
        var key = DocumentStorageKey.Create(context, content.Format);
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        content.Stream.Position = 0;
        await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await content.Stream.CopyToAsync(destination, cancellationToken);
        return new(StorageProvider.Local, null, key) { LocalPath = path };
    }

    public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Provider != StorageProvider.Local || reference.Bucket is not null)
            throw new ArgumentException("The storage reference does not belong to the configured provider.");
        return Task.FromResult<Stream>(new FileStream(GetPath(reference.Key), FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true));
    }

    private string GetPath(string key)
    {
        DocumentStorageKey.Validate(key);
        return Path.GetFullPath(Path.Combine(environment.WebRootPath, "documents", key.Replace('/', Path.DirectorySeparatorChar)));
    }
}
