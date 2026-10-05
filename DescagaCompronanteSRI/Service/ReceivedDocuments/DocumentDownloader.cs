using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class DocumentDownloader(IEnumerable<IDocumentDownloadStrategy> strategies) : IDocumentDownloader
{
    private readonly IReadOnlyDictionary<DownloadFormat, IDocumentDownloadStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Format);

    public Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document, DownloadFormat format) =>
        _strategies.TryGetValue(format, out var strategy)
            ? strategy.DownloadAsync(session, document)
            : Task.FromResult(OperationResult<DocumentContent>.Failure(
                ExtractionErrorCode.DownloadFailed, "Unsupported download format."));
}
