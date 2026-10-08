using System.Security.Cryptography;
using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Jobs;

public sealed class PersistentExtractionProgress(IDbContextFactory<CrawlerDbContext> factory,
    ISriDocumentJsonParser parser, IDocumentStorage storage, Guid extractionId, Guid attemptId,
    ReceivedDocumentsQuery query) : IExtractionProgress
{
    public async Task StageAsync(ExtractionStage stage, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Extractions.Where(x => x.Id == extractionId && x.ActiveAttemptId == attemptId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stage, stage)
                .SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow), token);
    }
    public async Task<ReceivedDocumentResponse?> FindSavedAsync(string key, int page, int row, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var saved = await db.Results.SingleOrDefaultAsync(x => x.ExtractionId == extractionId && x.Identity == key &&
            x.DownloadStatus == DocumentDownloadStatus.Downloaded, token);
        if (saved is null) return null;
        saved.SeenAttemptId = attemptId;
        var document = ExtractionJobs.Deserialize<ReceivedDocumentResponse>(saved.ResponseJson);
        document = CopyLocation(document, page, row);
        saved.ResponseJson = ExtractionJobs.Serialize(document);
        await db.SaveChangesAsync(token);
        // Retry conversion independently from the SRI download after an interrupted attempt.
        if (document.DownloadFormat == DownloadFormat.Xml && saved.JsonStatus == DocumentParseStatus.Failed && document.Storage is not null)
        {
            var reference = document.Storage with { LocalPath = document.FilePath };
            await using var stream = await storage.OpenReadAsync(reference, token);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, token);
            document.XmlHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));
            memory.Position = 0;
            using var reader = new StreamReader(memory);
            document.SourceXml = await reader.ReadToEndAsync(token);
            await SaveAsync(document, token);
        }
        return document;
    }
    private static ReceivedDocumentResponse CopyLocation(ReceivedDocumentResponse source, int page, int row)
    {
        var copy = new ReceivedDocumentResponse { PageNumber = page, RowIndex = row, Metadata = source.Metadata,
            DownloadFormat = source.DownloadFormat, DownloadStatus = source.DownloadStatus, ParseStatus = source.ParseStatus,
            Storage = source.Storage, FilePath = source.FilePath, ParsedDocument = source.ParsedDocument };
        copy.Errors.AddRange(source.Errors);
        return copy;
    }
    public async Task SaveAsync(ReceivedDocumentResponse result, CancellationToken token)
    {
        var key = result.Metadata?.AuthorizationNumber;
        var valid = key is { Length: 49 } && key.All(char.IsAsciiDigit);
        var identity = valid ? key! : $"row:{attemptId}:{result.PageNumber}:{result.RowIndex}";
        var conversion = result.DownloadFormat == DownloadFormat.Pdf
            ? new JsonConversion(DocumentParseStatus.NotApplicable)
            : result.DownloadStatus != DocumentDownloadStatus.Downloaded
                ? new JsonConversion(DocumentParseStatus.NotApplicable)
                : await parser.ParseAsync(result.SourceXml ?? "", query.DocumentType, key ?? "", token);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var job = await db.Extractions.SingleAsync(x => x.Id == extractionId, token);
        if (job.ActiveAttemptId != attemptId || job.JobStatus != JobStatus.Running)
            throw new OperationCanceledException("Extraction ownership was lost.", token);
        var canonicalType = query.DocumentType == DocumentType.RemissionGuideAlternative ? DocumentType.RemissionGuide : query.DocumentType;
        DocumentRecord? document = null;
        DocumentFile? file = null;
        DocumentConversion? version = null;
        if (valid)
        {
            document = await db.Documents.SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.TaxpayerId == query.User &&
                x.Direction == DocumentDirection.Received && x.DocumentType == canonicalType && x.AccessKey == key, token);
            if (document is null)
            {
                document = new() { CompanyId = query.CompanyId, TaxpayerId = query.User, Direction = DocumentDirection.Received,
                    DocumentType = canonicalType, AccessKey = key! };
                db.Documents.Add(document);
                await db.SaveChangesAsync(token);
            }
            if (conversion.DocumentJson is { } json && json.TryGetProperty("infoTributaria", out var tax) && tax.TryGetProperty("ruc", out var issuer))
                document.IssuerTaxpayerId = issuer.ValueKind == JsonValueKind.String ? issuer.GetString() : issuer.GetRawText();
            if (result.Storage is not null && result.DownloadStatus == DocumentDownloadStatus.Downloaded)
            {
                file = await db.Files.SingleOrDefaultAsync(x => x.DocumentId == document.Id && x.Format == result.DownloadFormat &&
                    x.Year == query.Year && x.Month == query.Month, token);
                if (file is null) { file = new() { DocumentId = document.Id, Format = result.DownloadFormat, Year = query.Year, Month = query.Month }; db.Files.Add(file); }
                file.StorageJson = ExtractionJobs.Serialize(result.Storage);
                file.LocalPath = result.FilePath;
            }
            if (result.DownloadFormat == DownloadFormat.Xml && result.DownloadStatus == DocumentDownloadStatus.Downloaded)
            {
                version = new() { DocumentId = document.Id, XmlHash = result.XmlHash ?? "", Status = conversion.Status,
                    DocumentJson = conversion.DocumentJson?.GetRawText(), ParserName = conversion.ParserName,
                    ParserVersion = conversion.ParserVersion, ErrorCode = conversion.ErrorCode, CreatedAt = DateTimeOffset.UtcNow };
                db.Conversions.Add(version);
            }
            await db.SaveChangesAsync(token);
        }
        var row = await db.Results.SingleOrDefaultAsync(x => x.ExtractionId == extractionId && x.Identity == identity, token);
        if (row is null) { row = new() { ExtractionId = extractionId, Identity = identity }; db.Results.Add(row); }
        row.SeenAttemptId = attemptId;
        row.DocumentId = document?.Id;
        row.FileId = file?.Id;
        row.ConversionId = version?.Id;
        row.DownloadStatus = result.DownloadStatus;
        row.JsonStatus = conversion.Status;
        row.ResponseJson = ExtractionJobs.Serialize(result);
        foreach (var error in result.Errors)
            db.Failures.Add(new() { ExtractionId = extractionId, AttemptId = attemptId, CreatedAt = DateTimeOffset.UtcNow, ErrorJson = ExtractionJobs.Serialize(error) });
        if (conversion.ErrorCode is not null && result.DownloadStatus == DocumentDownloadStatus.Downloaded)
            db.Failures.Add(new() { ExtractionId = extractionId, AttemptId = attemptId, CreatedAt = DateTimeOffset.UtcNow,
                ErrorJson = ExtractionJobs.Serialize(new { code = conversion.ErrorCode, message = "JSON conversion could not be completed.", result.PageNumber, result.RowIndex }) });
        job.LastActivityAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
    public async Task CheckpointAsync(ReceivedDocumentsResponse response, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var job = await db.Extractions.SingleAsync(x => x.Id == extractionId && x.ActiveAttemptId == attemptId, token);
        job.QuerySucceeded = response.QuerySucceeded;
        job.BusinessName = response.BusinessName;
        job.PaginationJson = ExtractionJobs.Serialize(response.Pagination);
        job.LastActivityAt = DateTimeOffset.UtcNow;
        foreach (var error in response.Errors)
            db.Failures.Add(new() { ExtractionId = extractionId, AttemptId = attemptId, CreatedAt = DateTimeOffset.UtcNow, ErrorJson = ExtractionJobs.Serialize(error) });
        response.Errors.Clear();
        await db.SaveChangesAsync(token);
    }
    public async Task<bool> HasUnseenDocumentsAsync(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Results.AnyAsync(x => x.ExtractionId == extractionId && x.DocumentId != null && x.SeenAttemptId != attemptId, token);
    }
}
