using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Contracts;

public interface IReceivedDocumentsPage
{
    Task<bool> OpenAsync(IReceivedDocumentsSession session);
    Task<OperationResult<int>> QueryAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query);
    Task<OperationResult<ReceivedDocumentReference>> ReadRowAsync(IReceivedDocumentsSession session, int rowIndex);
}
