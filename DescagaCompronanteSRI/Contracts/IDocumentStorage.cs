using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentStorage
{
    Task<string> SaveAsync(string taxpayerId, string authorizationNumber, DocumentContent content);
    Task<Stream> OpenReadAsync(string taxpayerId, string authorizationNumber, DownloadFormat format);
}
