using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;

namespace DescagaCompronanteSRI.Tests.Storage;

public sealed class AtomicLocalStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedCopy_PreservesPreviousFileAndRemovesOnlyItsTemporary(bool cancel)
    {
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        await using var original = new DocumentContent(new MemoryStream("previous complete file"u8.ToArray()), DownloadFormat.Xml);
        var reference = await storage.SaveAsync(StorageTestSupport.Context, original);
        var directory = Path.GetDirectoryName(reference.LocalPath!)!;
        var orphan = Path.Combine(directory, ".sri-write-unrelated.tmp");
        await File.WriteAllTextAsync(orphan, "unrelated");
        using var cancellation = new CancellationTokenSource();
        await using var replacement = new DocumentContent(new InterruptedStream(cancel ? cancellation : null), DownloadFormat.Xml);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.SaveAsync(StorageTestSupport.Context, replacement, cancellation.Token));
        else await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(StorageTestSupport.Context, replacement));
        Assert.Equal("previous complete file", await File.ReadAllTextAsync(reference.LocalPath!));
        Assert.Equal([orphan], Directory.GetFiles(directory, ".sri-write-*.tmp"));
    }

    [Fact]
    public async Task Replacement_WithActiveReader_PublishesNewBytesAndKeepsOldReaderComplete()
    {
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        await using var original = new DocumentContent(new MemoryStream("old complete"u8.ToArray()), DownloadFormat.Pdf);
        var reference = await storage.SaveAsync(StorageTestSupport.Context, original);
        var before = await storage.InspectAsync(reference);
        await using var reader = await storage.OpenReadAsync(reference);
        await using var replacement = new DocumentContent(new MemoryStream("new complete PDF"u8.ToArray()), DownloadFormat.Pdf);
        var next = await storage.SaveAsync(StorageTestSupport.Context, replacement);
        using var text = new StreamReader(reader);
        Assert.Equal("old complete", await text.ReadToEndAsync());
        Assert.Equal("new complete PDF", await File.ReadAllTextAsync(next.LocalPath!));
        var after = await storage.InspectAsync(next);
        Assert.Equal(StorageInspectionStatus.Exists, after.Status);
        Assert.Equal(next.Revision, after.Revision);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(next.LocalPath!)!, ".sri-write-*.tmp"));
    }

    [Fact]
    public async Task Inspection_DistinguishesMissingFromExistingAndIgnoresUntrustedPhysicalPath()
    {
        using var environment = new StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        var missing = new DocumentStorageReference(StorageProvider.Local, null, DocumentStorageKey.Create(StorageTestSupport.Context, DownloadFormat.Xml));
        Assert.Equal(StorageInspectionStatus.Missing, (await storage.InspectAsync(missing)).Status);
        await using var content = new DocumentContent(new MemoryStream("bytes"u8.ToArray()), DownloadFormat.Xml);
        var reference = await storage.SaveAsync(StorageTestSupport.Context, content);
        var inspection = await storage.InspectAsync(reference with { LocalPath = "/invalid" });
        Assert.Equal(5, inspection.SizeBytes);
        Assert.NotNull(inspection.Revision);
        Assert.False(storage.CanRead(new(StorageProvider.S3, "other", reference.Key)));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.InspectAsync(reference with { Key = "../outside" }));
    }

    private sealed class InterruptedStream(CancellationTokenSource? cancel) : MemoryStream
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await destination.WriteAsync("incomplete replacement"u8.ToArray(), cancellationToken);
            if (cancel is not null) { cancel.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            throw new IOException("Synthetic mid-copy failure.");
        }
    }
}
