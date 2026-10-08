using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.ReceivedDocuments;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public class DocumentDownloaderTests
{
    [Theory]
    [InlineData(DownloadFormat.Xml)]
    [InlineData(DownloadFormat.Pdf)]
    public async Task Download_SelectsExactlyOneStrategy(DownloadFormat format)
    {
        var xml = new Strategy(DownloadFormat.Xml);
        var pdf = new Strategy(DownloadFormat.Pdf);
        var downloader = new DocumentDownloader([xml, pdf]);
        var result = await downloader.DownloadAsync(null!, new(new(), "", "", ""), format);
        Assert.True(result.IsSuccess);
        await using var content = result.Value!;
        Assert.Equal(format, content.Format);
        Assert.Equal(format == DownloadFormat.Xml ? 1 : 0, xml.Calls);
        Assert.Equal(format == DownloadFormat.Pdf ? 1 : 0, pdf.Calls);
    }

    [Fact]
    public async Task Download_RejectsUnknownFormat()
    {
        var strategy = new Strategy(DownloadFormat.Xml);
        var result = await new DocumentDownloader([strategy]).DownloadAsync(
            null!, new(new(), "", "", ""), (DownloadFormat)999);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, strategy.Calls);
        Assert.Equal(ExtractionErrorCode.DownloadFailed, result.Error!.Code);
    }

    private sealed class Strategy(DownloadFormat format) : IDocumentDownloadStrategy
    {
        public DownloadFormat Format => format;
        public int Calls { get; private set; }
        public Task<OperationResult<DocumentContent>> DownloadAsync(
            IReceivedDocumentsSession session, ReceivedDocumentReference document, CancellationToken token = default)
        {
            Calls++;
            return Task.FromResult(OperationResult<DocumentContent>.Success(new(new MemoryStream(), Format)));
        }
    }
}
