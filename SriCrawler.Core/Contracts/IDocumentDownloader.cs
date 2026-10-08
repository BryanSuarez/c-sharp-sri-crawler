using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentDownloader
{
    Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document, DownloadFormat format, CancellationToken token = default);
}

public interface IDocumentDownloadStrategy
{
    DownloadFormat Format { get; }
    Task<OperationResult<DocumentContent>> DownloadAsync(
        IReceivedDocumentsSession session, ReceivedDocumentReference document, CancellationToken token = default);
}
