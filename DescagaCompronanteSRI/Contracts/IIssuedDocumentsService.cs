using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Responses;

namespace DescagaCompronanteSRI.Contracts;

public interface IIssuedDocumentsService
{
    Task<SriIssuedUserResponse?> QueryAsync(IssuedDocumentsQuery query, string destination);
}
