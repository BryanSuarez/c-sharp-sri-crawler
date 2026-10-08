using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Contracts;

public interface IReceivedDocumentsPage
{
    Task<bool> OpenAsync(IReceivedDocumentsSession session);
    Task<OperationResult<ReceivedDocumentsPageSnapshot>> QueryAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query);
    Task<OperationResult<ReceivedDocumentsPageSnapshot>> MoveNextAsync(
        IReceivedDocumentsSession session, ReceivedDocumentsPageSnapshot current);
}
