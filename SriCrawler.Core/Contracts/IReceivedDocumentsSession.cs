using DescagaCompronanteSRI.Models.Dtos;
using Microsoft.Playwright;
namespace DescagaCompronanteSRI.Contracts;

public interface IReceivedDocumentsSession : IAsyncDisposable
{
    IPage Page { get; }
    Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query);
    Task<string> GetPortalBodyAsync();
    Task SubmitPortalFormAsync(string body);
    Task CloseModalAsync();
}

public interface IReceivedDocumentsSessionFactory
{
    IReceivedDocumentsSession Create();
}
