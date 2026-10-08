using System.Security.Cryptography;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Jobs;

// Internal operation: no SRI session, public endpoint or historical-result mutation.
public sealed class DocumentReprocessor(IDbContextFactory<CrawlerDbContext> factory,
    IDocumentStorage storage, ISriDocumentJsonParser parser)
{
    public async Task<long> ReprocessAsync(long fileId, string companyId, string taxpayerId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var item = await (from file in db.Files join doc in db.Documents on file.DocumentId equals doc.Id
            where file.Id == fileId && doc.CompanyId == companyId && doc.TaxpayerId == taxpayerId && file.Format == DownloadFormat.Xml
            select new { File = file, Document = doc }).SingleOrDefaultAsync(token)
            ?? throw new InvalidOperationException("XML document not found.");
        var reference = ExtractionJobs.Deserialize<DocumentStorageReference>(item.File.StorageJson) with { LocalPath = item.File.LocalPath };
        await using var stream = await storage.OpenReadAsync(reference, token);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token);
        var hash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        memory.Position = 0;
        using var reader = new StreamReader(memory);
        var conversion = await parser.ParseAsync(await reader.ReadToEndAsync(token), item.Document.DocumentType, item.Document.AccessKey, token);
        var record = new DocumentConversion { DocumentId = item.Document.Id, XmlHash = hash, Status = conversion.Status,
            DocumentJson = conversion.DocumentJson?.GetRawText(), ParserName = conversion.ParserName,
            ParserVersion = conversion.ParserVersion, ErrorCode = conversion.ErrorCode, CreatedAt = DateTimeOffset.UtcNow };
        db.Conversions.Add(record);
        await db.SaveChangesAsync(token);
        return record.Id;
    }
}
