using DescagaCompronanteSRI.Diagnostics;
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
    ReceivedDocumentsQuery query, IDocumentValidator validator, Microsoft.Extensions.Options.IOptions<DocumentValidationOptions> validationOptions, IDocumentReuseResolver? reuseResolver = null) : IExtractionProgress
{
    public async Task StageAsync(ExtractionStage stage, CancellationToken token)
    {
        ExtractionDiagnostics.Current?.SetStage(stage);
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Extractions.Where(x => x.Id == extractionId && x.ActiveAttemptId == attemptId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stage, stage)
                .SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow), token);
        if (ExtractionDiagnostics.Current is { } diagnostics)
            await AttemptDiagnosticsStore.SaveAsync(db, extractionId, attemptId, diagnostics.Snapshot(), token);
    }
    private readonly IDocumentReuseResolver reuse = reuseResolver ?? new DocumentReuseResolver(factory, storage, validator, validationOptions,
        new DescagaCompronanteSRI.Services.ReceivedDocuments.DocumentParser());

    public Task PreparePageAsync(IReadOnlyList<string> keys, CancellationToken token) => reuse.PreparePageAsync(query, keys, token);

    public async Task<ReceivedDocumentResponse?> FindSavedAsync(string key, int page, int row, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var previous = await db.Results.AsNoTracking().SingleOrDefaultAsync(x => x.ExtractionId == extractionId && x.Identity == key &&
            x.DownloadStatus == DocumentDownloadStatus.Downloaded, token);
        var metadata = previous is null ? new ReceivedDocumentMetadata { AuthorizationNumber = key } :
            ExtractionJobs.Deserialize<ReceivedDocumentResponse>(previous.ResponseJson).Metadata ?? new ReceivedDocumentMetadata { AuthorizationNumber = key };
        return await ResolveAsync(metadata, page, row, previous, token);
    }

    public async Task<ReceivedDocumentResponse?> FindSavedAsync(ReceivedDocumentMetadata metadata, int page, int row, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var previous = await db.Results.AsNoTracking().SingleOrDefaultAsync(x => x.ExtractionId == extractionId && x.Identity == metadata.AuthorizationNumber &&
            x.DownloadStatus == DocumentDownloadStatus.Downloaded, token);
        return await ResolveAsync(metadata, page, row, previous, token);
    }
    private async Task<ReceivedDocumentResponse?> ResolveAsync(ReceivedDocumentMetadata metadata, int page, int row, ExtractionDocument? previous, CancellationToken token)
    {
        var document = await reuse.ResolveAsync(query, metadata, page, row, previous, token);
        if (document is not null) await SaveAsync(document, token);
        return document;
    }
    public async Task SaveAsync(ReceivedDocumentResponse result, CancellationToken token)
    {
        if (result.DownloadStatus == DocumentDownloadStatus.Downloaded &&
            (result.Validation.Status != DocumentValidationStatus.Valid || result.StorageStatus != DocumentStorageStatus.Stored || result.Storage is null))
            throw new InvalidOperationException("A downloaded document requires validation and confirmed storage.");
        var key = result.Metadata?.AuthorizationNumber;
        var valid = key is { Length: 49 } && key.All(char.IsAsciiDigit);
        var identity = valid ? key! : $"row:{attemptId}:{result.PageNumber}:{result.RowIndex}";
        if (result.DownloadStatus == DocumentDownloadStatus.Downloaded && result.AcquisitionSource == DocumentAcquisitionSource.NotAcquired)
            result.AcquisitionSource = DocumentAcquisitionSource.Downloaded;
        await using var db = await factory.CreateDbContextAsync(token);
        DocumentConversion? cached = null;
        if (valid && result.DownloadStatus == DocumentDownloadStatus.Downloaded && result.DownloadFormat == DownloadFormat.Xml)
        {
            var canonicalTypeForCache = DocumentReuseResolver.CanonicalType(query.DocumentType);
            var documentId = await db.Documents.Where(x => x.CompanyId == query.CompanyId && x.TaxpayerId == query.User &&
                x.Direction == DocumentDirection.Received && x.DocumentType == canonicalTypeForCache && x.AccessKey == key)
                .Select(x => (long?)x.Id).SingleOrDefaultAsync(token);
            if (documentId is long id && !string.IsNullOrWhiteSpace(result.XmlHash))
                cached = await DocumentReuseResolver.CompatibleConversionAsync(db, id, result.XmlHash, token);
        }
        using var cachedJson = cached is null ? null : JsonDocument.Parse(cached.DocumentJson!);
        var conversion = result.DownloadFormat == DownloadFormat.Pdf || result.DownloadStatus != DocumentDownloadStatus.Downloaded
            ? new JsonConversion(DocumentParseStatus.NotApplicable)
            : cached is not null ? new JsonConversion(DocumentParseStatus.Parsed, cachedJson!.RootElement.Clone(), cached.ParserName, cached.ParserVersion)
            : await parser.ParseAsync(result.SourceXml ?? "", query.DocumentType, key ?? "", token);
        using var persistence = ExtractionDiagnostics.StartOperation(DiagnosticOperation.Persistence, token);
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
                file = result.ReusedFileId is long reusedId
                    ? await db.Files.SingleAsync(x => x.Id == reusedId && x.DocumentId == document.Id && x.Format == result.DownloadFormat, token)
                    : await db.Files.SingleOrDefaultAsync(x => x.DocumentId == document.Id && x.Format == result.DownloadFormat &&
                        x.Year == query.Year && x.Month == query.Month, token);
                if (file is null) { file = new() { DocumentId = document.Id, Format = result.DownloadFormat, Year = query.Year, Month = query.Month }; db.Files.Add(file); }
                file.StorageJson = ExtractionJobs.Serialize(result.Storage);
                file.LocalPath = result.FilePath;
                SetFileValidation(file, result.Validation);
                file.StorageRevision = result.Storage.Revision;
                file.ValidationProfile = DocumentReuseResolver.Profile(validator, validationOptions.Value, result.DownloadFormat);
                if (result.ReusedFileId is null) file.StoredAt = DateTimeOffset.UtcNow;
            }
            if (result.DownloadFormat == DownloadFormat.Xml && result.DownloadStatus == DocumentDownloadStatus.Downloaded)
            {
                if (cached is not null) version = await db.Conversions.SingleAsync(x => x.Id == cached.Id, token);
                else
                {
                    version = new() { DocumentId = document.Id, XmlHash = result.XmlHash ?? "", Status = conversion.Status,
                        DocumentJson = conversion.DocumentJson?.GetRawText(), ParserName = conversion.ParserName,
                        ParserVersion = conversion.ParserVersion, ErrorCode = conversion.ErrorCode, CreatedAt = DateTimeOffset.UtcNow };
                    db.Conversions.Add(version);
                }
            }
            await db.SaveChangesAsync(token);
        }
        var row = await db.Results.SingleOrDefaultAsync(x => x.ExtractionId == extractionId && x.Identity == identity, token);
        if (row is null) { row = new() { ExtractionId = extractionId, Identity = identity }; db.Results.Add(row); }
        row.SeenAttemptId = attemptId;
        row.DocumentId = document?.Id;
        row.FileId = file?.Id;
        row.ConversionId = version?.Id;
        row.AcquisitionSource = result.AcquisitionSource;
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
        persistence.Complete();
    }
    private static void SetFileValidation(DocumentFile file, DocumentValidationResult validation)
    {
        file.ValidationJson = ExtractionJobs.Serialize(validation);
        file.ValidationStatus = validation.Status;
        file.Sha256 = validation.Sha256;
        file.SizeBytes = validation.SizeBytes;
    }
    public Task CheckpointAsync(ReceivedDocumentsResponse response, CancellationToken token) =>
        ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Persistence, () => CheckpointCoreAsync(response, token));
    private async Task CheckpointCoreAsync(ReceivedDocumentsResponse response, CancellationToken token)
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
        if (ExtractionDiagnostics.Current is { } diagnostics)
            await AttemptDiagnosticsStore.SaveAsync(db, extractionId, attemptId, diagnostics.Snapshot(), token);
    }
    public async Task<bool> HasUnseenDocumentsAsync(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Results.AnyAsync(x => x.ExtractionId == extractionId && x.DocumentId != null && x.SeenAttemptId != attemptId, token);
    }
}
