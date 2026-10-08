using System.Globalization;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Services.Storage;

public sealed class LocalDocumentStorage(IHostEnvironment environment) : IDocumentStorage
{
    public bool CanRead(DocumentStorageReference reference) => reference.Provider == StorageProvider.Local && reference.Bucket is null;

    public Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(reference)) throw new ArgumentException("The storage reference does not belong to the configured provider.");
        try
        {
            var info = new FileInfo(GetPath(reference.Key));
            // File.Exists suppresses permission errors; open the file to distinguish them from absence.
            using var handle = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            info.Refresh();
            return Task.FromResult(new DocumentStorageInspection(StorageInspectionStatus.Exists, handle.Length,
                string.Create(CultureInfo.InvariantCulture, $"{handle.Length}:{info.LastWriteTimeUtc.Ticks}")));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { return Task.FromResult(new DocumentStorageInspection(StorageInspectionStatus.Missing)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Task.FromResult(new DocumentStorageInspection(StorageInspectionStatus.Failed)); }
    }

    public async Task<DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default)
    {
        var key = DocumentStorageKey.Create(context, content.Format);
        var path = GetPath(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".sri-write-{Guid.NewGuid():N}.tmp");
        try
        {
            content.Stream.Position = 0;
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.Stream.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Both paths are in the same directory. Never delete or truncate the previous destination.
            File.Move(temporary, path, overwrite: true);
            var info = new FileInfo(path);
            return new(StorageProvider.Local, null, key) { LocalPath = path,
                Revision = string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}") };
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* An orphan is never published or reused. */ }
        }
    }

    public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(reference)) throw new ArgumentException("The storage reference does not belong to the configured provider.");
        return Task.FromResult<Stream>(new FileStream(GetPath(reference.Key), FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, bufferSize: 81920, useAsync: true));
    }

    private string GetPath(string key)
    {
        DocumentStorageKey.Validate(key);
        var root = (environment as IWebHostEnvironment)?.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        return Path.GetFullPath(Path.Combine(root, "documents", key.Replace('/', Path.DirectorySeparatorChar)));
    }
}
