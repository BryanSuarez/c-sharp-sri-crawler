using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;

namespace DescagaCompronanteSRI.Contracts;

public interface IDocumentStorage
{
    bool CanRead(DocumentStorageReference reference);
    Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default);
    Task<DocumentStorageReference> SaveAsync(
        DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default);
}
