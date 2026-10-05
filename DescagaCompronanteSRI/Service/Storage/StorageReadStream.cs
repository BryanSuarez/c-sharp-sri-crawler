namespace DescagaCompronanteSRI.Services.Storage;

// The S3 response owns the network stream and must live until its consumer finishes reading.
internal sealed class StorageReadStream(Stream stream, IDisposable owner) : Stream
{
    private bool disposed;
    public override bool CanRead => !disposed && stream.CanRead;
    public override bool CanSeek => !disposed && stream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => stream.Length;
    public override long Position { get => stream.Position; set => stream.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            owner.Dispose();
        }
        base.Dispose(disposing);
    }
}
