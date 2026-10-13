using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Security;

public enum DocumentFileAccessStatus { Available, NotFound, Unavailable }
public sealed record DocumentFileAccess(DocumentFileAccessStatus Status, Stream? Content = null, string? ContentType = null, string? FileName = null);
public interface IExtractionDocumentFiles
{
    Task<DocumentFileAccess> OpenAsync(Guid extractionId, long resultId, string companyId, CancellationToken token);
}

public sealed class ExtractionDocumentFiles(CrawlerDbContext db, IDocumentStorage storage) : IExtractionDocumentFiles
{
    public async Task<DocumentFileAccess> OpenAsync(Guid extractionId, long resultId, string companyId, CancellationToken token)
    {
        try
        {
            var file = await (from extraction in db.Extractions.AsNoTracking()
                join result in db.Results.AsNoTracking() on extraction.Id equals result.ExtractionId
                join document in db.Documents.AsNoTracking() on result.DocumentId equals document.Id
                join stored in db.Files.AsNoTracking() on result.FileId equals stored.Id
                where extraction.Id == extractionId && extraction.CompanyId == companyId && result.Id == resultId
                    && document.CompanyId == companyId && document.TaxpayerId == extraction.TaxpayerId
                    && document.Direction == DocumentDirection.Received && stored.DocumentId == document.Id
                    && result.Identity == document.AccessKey && result.DownloadStatus == DocumentDownloadStatus.Downloaded
                select new { Stored = stored, Document = document }).SingleOrDefaultAsync(token);
            if (file is null) return new(DocumentFileAccessStatus.NotFound);
            var reference = ExtractionJobs.Deserialize<DocumentStorageReference>(file.Stored.StorageJson);
            var expected = DocumentStorageKey.Create(new DocumentStorageContext(file.Document.CompanyId, file.Document.TaxpayerId,
                file.Stored.Year, file.Stored.Month, file.Document.Direction, file.Document.DocumentType, file.Document.AccessKey), file.Stored.Format);
            if (reference?.Key != expected || !storage.CanRead(reference)) return new(DocumentFileAccessStatus.Unavailable);
            var stream = await storage.OpenReadAsync(reference, token);
            return new(DocumentFileAccessStatus.Available, stream, DocumentStorageKey.ContentType(file.Stored.Format),
                $"{file.Document.AccessKey}.{(file.Stored.Format == DownloadFormat.Xml ? "xml" : "pdf")}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(DocumentFileAccessStatus.NotFound); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException || SecurityPersistence.IsDatabaseFailure(e))
        { return new(DocumentFileAccessStatus.Unavailable); }
    }
}
