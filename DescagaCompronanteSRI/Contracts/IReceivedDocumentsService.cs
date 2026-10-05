using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Responses;
namespace DescagaCompronanteSRI.Contracts;

public interface IReceivedDocumentsService
{
    Task<ReceivedDocumentsResponse> QueryAsync(ReceivedDocumentsQuery query);
}
