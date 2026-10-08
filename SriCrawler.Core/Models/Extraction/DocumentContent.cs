using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Extraction;

public sealed class DocumentContent(Stream stream, DownloadFormat format, string? sourceXml = null) : IAsyncDisposable
{
    public string? SourceXml { get; } = sourceXml;
    public Stream Stream { get; } = stream;
    public DownloadFormat Format { get; } = format;
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

public sealed record DocumentParseResult(
    DocumentParseStatus Status, object? Document = null, ExtractionError? Error = null);
