namespace DescagaCompronanteSRI.Validation;

internal static class BoundedDocumentStream
{
    public static async Task CopyAsync(Stream source, Stream destination, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) != 0)
        {
            total += read;
            if (total > limit) throw new DocumentSizeLimitException();
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }
}
internal sealed class DocumentSizeLimitException : IOException;
