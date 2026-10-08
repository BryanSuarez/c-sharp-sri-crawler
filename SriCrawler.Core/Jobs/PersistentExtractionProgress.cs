using DescagaCompronanteSRI.Validation;
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
    ReceivedDocumentsQuery query, IDocumentValidator validator, Microsoft.Extensions.Options.IOptions<DocumentValidationOptions> validationOptions) : IExtractionProgress
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
        var document = CopyLocation(ExtractionJobs.Deserialize<ReceivedDocumentResponse>(saved.ResponseJson), page, row);
        var file = saved.FileId is null ? null : await db.Files.SingleOrDefaultAsync(x => x.Id == saved.FileId, token);
        var requiresValidation = file is null || file.ValidationStatus != DocumentValidationStatus.Valid ||
            string.IsNullOrWhiteSpace(document.Validation.Sha256) || document.Validation.Status != DocumentValidationStatus.Valid ||
            document.Validation.ValidatorVersion != validator.Version || document.Validation.Sha256 != file?.Sha256;
        var requiresConversion = document.DownloadFormat == DownloadFormat.Xml && saved.JsonStatus == DocumentParseStatus.Failed;
        if (document.Storage is null) return null;
        if (requiresValidation || requiresConversion)
        {
            try
            {
                var reference = document.Storage with { LocalPath = document.FilePath };
                await using var stream = await storage.OpenReadAsync(reference, token);
                var limit = document.DownloadFormat == DownloadFormat.Xml ? validationOptions.Value.MaxXmlBytes : validationOptions.Value.MaxPdfBytes;
                var memory = new MemoryStream();
                await using var content = new DocumentContent(memory, document.DownloadFormat);
                await BoundedDocumentStream.CopyAsync(stream, memory, limit, token);
                memory.Position = 0;
                // Conversion retries read the file again, so bind fresh validation to those bytes.
                document.Validation = await validator.ValidateAsync(content, query.DocumentType, key, token);
                if (document.Validation.Status != DocumentValidationStatus.Valid) return null;
                document.StorageStatus = DocumentStorageStatus.Stored;
                document.XmlHash = document.Validation.Sha256;
                if (requiresConversion)
                {
                    using var reader = new StreamReader(memory, leaveOpen: true);
                    document.SourceXml = await reader.ReadToEndAsync(token);
                    await SaveAsync(document, token);
                }
            }
            catch (IOException) { return null; }
        }
        saved.SeenAttemptId = attemptId;
        saved.ValidationStatus = document.Validation.Status;
        saved.StorageStatus = document.StorageStatus;
        saved.ValidationJson = ExtractionJobs.Serialize(document.Validation);
        saved.FileHash = document.Validation.Sha256;
        saved.ResponseJson = ExtractionJobs.Serialize(document);
        if (file is not null && (requiresValidation || requiresConversion)) SetFileValidation(file, document.Validation);
        await db.SaveChangesAsync(token);
        return document;
    }
    private static ReceivedDocumentResponse CopyLocation(ReceivedDocumentResponse source, int page, int row)
    {
        var copy = new ReceivedDocumentResponse { PageNumber = page, RowIndex = row, Metadata = source.Metadata,
            DownloadFormat = source.DownloadFormat, DownloadStatus = source.DownloadStatus, ParseStatus = source.ParseStatus,
            Storage = source.Storage, FilePath = source.FilePath, ParsedDocument = source.ParsedDocument,
            Validation = source.Validation, StorageStatus = source.StorageStatus };
        copy.Errors.AddRange(source.Errors);
        return copy;
    }
    public async Task SaveAsync(ReceivedDocumentResponse result, CancellationToken token)
    {
        if (result.DownloadStatus == DocumentDownloadStatus.Downloaded &&
            (result.Validation.Status != DocumentValidationStatus.Valid || result.StorageStatus != DocumentStorageStatus.Stored || result.Storage is null))
            throw new InvalidOperationException("A downloaded document requires validation and confirmed storage.");
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
                SetFileValidation(file, result.Validation);
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
        row.ValidationStatus = result.Validation.Status;
        row.StorageStatus = result.StorageStatus;
        row.MetadataParseStatus = result.MetadataParseStatus;
        row.ValidationJson = ExtractionJobs.Serialize(result.Validation);
        row.FileHash = result.Validation.Sha256;
        row.SourceValuesJson = result.Metadata?.SourceValues is { } values ? ExtractionJobs.Serialize(values) : null;
        row.Amount = result.Metadata?.Amount; row.Taxes = result.Metadata?.Taxes; row.Total = result.Metadata?.Total;
        row.IssuedDate = result.Metadata?.IssuedDate;
        row.AuthorizedAt = result.Metadata?.AuthorizedAtIso?.ToUniversalTime();
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
    private static void SetFileValidation(DocumentFile file, DocumentValidationResult validation)
    {
        file.ValidationJson = ExtractionJobs.Serialize(validation);
        file.ValidationStatus = validation.Status;
        file.Sha256 = validation.Sha256;
        file.SizeBytes = validation.SizeBytes;
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
