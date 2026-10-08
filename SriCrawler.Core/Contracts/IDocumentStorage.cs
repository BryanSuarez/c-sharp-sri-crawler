using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;

namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentStorage
{
    Task<DocumentStorageReference> SaveAsync(
        DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default);
}
