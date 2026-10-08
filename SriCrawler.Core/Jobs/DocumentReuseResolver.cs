using System.Text.Json;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using DescagaCompronanteSRI.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Jobs;

public interface IDocumentReuseResolver
{
    Task PreparePageAsync(ReceivedDocumentsQuery query, IReadOnlyList<string> keys, CancellationToken token);
    Task<ReceivedDocumentResponse?> ResolveAsync(ReceivedDocumentsQuery query, ReceivedDocumentMetadata metadata,
        int page, int row, ExtractionDocument? previous, CancellationToken token);
}

public sealed class DocumentReuseResolver(IDbContextFactory<CrawlerDbContext> factory, IDocumentStorage storage,
    IDocumentValidator validator, IOptions<DocumentValidationOptions> options, IDocumentParser documentParser) : IDocumentReuseResolver
{
    private readonly Dictionary<string, List<DocumentFile>> candidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> preparedKeys = new(StringComparer.Ordinal);

    public static string Profile(IDocumentValidator validator, DocumentValidationOptions options, DownloadFormat format) =>
        System.FormattableString.Invariant($"{validator.Version}:{format}:{(format == DownloadFormat.Xml ? options.MaxXmlBytes : options.MaxPdfBytes)}:{(format == DownloadFormat.Pdf ? options.MaxPdfPages : 0)}");

    public async Task PreparePageAsync(ReceivedDocumentsQuery query, IReadOnlyList<string> keys, CancellationToken token)
    {
        candidates.Clear(); preparedKeys.Clear();
        foreach (var key in keys) preparedKeys.Add(key);
        if (keys.Count == 0) return;
        await using var db = await factory.CreateDbContextAsync(token);
        var type = CanonicalType(query.DocumentType);
        var files = await (from doc in db.Documents.AsNoTracking() join file in db.Files.AsNoTracking() on doc.Id equals file.DocumentId
            where doc.CompanyId == query.CompanyId && doc.TaxpayerId == query.User && doc.Direction == DocumentDirection.Received &&
                doc.DocumentType == type && keys.Contains(doc.AccessKey) && file.Format == query.DownloadFormat
            orderby file.Year == query.Year && file.Month == query.Month descending, file.StoredAt.HasValue descending, file.StoredAt descending, file.Id descending
            select new { doc.AccessKey, File = file }).ToListAsync(token);
        foreach (var item in files)
        {
            if (!candidates.TryGetValue(item.AccessKey, out var list)) candidates[item.AccessKey] = list = [];
            list.Add(item.File);
        }
    }

    public async Task<ReceivedDocumentResponse?> ResolveAsync(ReceivedDocumentsQuery query, ReceivedDocumentMetadata metadata,
        int page, int row, ExtractionDocument? previous, CancellationToken token)
    {
        if (previous is null && query.DownloadPolicy == DownloadPolicy.Refresh) return null;
        var key = metadata.AuthorizationNumber;
        if (!preparedKeys.Contains(key)) await PreparePageAsync(query, [key], token);
        if (!candidates.TryGetValue(key, out var files)) return null;
        var ordered = previous?.FileId is long id ? files.OrderByDescending(x => x.Id == id) : files.AsEnumerable();
        foreach (var file in ordered)
        {
            token.ThrowIfCancellationRequested();
            var reference = ExtractionJobs.Deserialize<DocumentStorageReference>(file.StorageJson) with { LocalPath = file.LocalPath };
            if (!storage.CanRead(reference)) continue;
            var inspection = await storage.InspectAsync(reference, token);
            if (inspection.Status == StorageInspectionStatus.Missing) continue;
            var response = new ReceivedDocumentResponse { PageNumber = page, RowIndex = row, Metadata = metadata,
                DownloadFormat = query.DownloadFormat, AcquisitionSource = previous?.AcquisitionSource ?? DocumentAcquisitionSource.Reused };
            response.Errors.AddRange(metadata.Errors.Select(e => e with { PageNumber = page, RowIndex = row }));
            if (inspection.Status != StorageInspectionStatus.Exists) return VerificationFailure(response);
            var validation = ExtractionJobs.Deserialize<DocumentValidationResult>(file.ValidationJson);
            var requiresValidation = file.ValidationStatus != DocumentValidationStatus.Valid || validation.Status != DocumentValidationStatus.Valid ||
                string.IsNullOrWhiteSpace(file.Sha256) || validation.Sha256 != file.Sha256 || validation.ValidatorVersion != validator.Version ||
                file.ValidationProfile != Profile(validator, options.Value, query.DownloadFormat) ||
                string.IsNullOrWhiteSpace(file.StorageRevision) || string.IsNullOrWhiteSpace(inspection.Revision) ||
                inspection.Revision != file.StorageRevision || inspection.SizeBytes != file.SizeBytes;
            DocumentContent? content = null;
            try
            {
                async Task<bool> ReadAndValidateAsync()
                {
                    if (content is not null) return true;
                    await using var input = await storage.OpenReadAsync(reference, token);
                    var memory = new MemoryStream();
                    content = new DocumentContent(memory, query.DownloadFormat);
                    var limit = query.DownloadFormat == DownloadFormat.Xml ? options.Value.MaxXmlBytes : options.Value.MaxPdfBytes;
                    await BoundedDocumentStream.CopyAsync(input, memory, limit, token);
                    memory.Position = 0;
                    validation = await validator.ValidateAsync(content, query.DocumentType, key, token);
                    if (validation.Status != DocumentValidationStatus.Valid) return false;
                    var after = await storage.InspectAsync(reference, token);
                    if (after.Status != StorageInspectionStatus.Exists || after.Revision != inspection.Revision || after.SizeBytes != inspection.SizeBytes)
                        throw new IOException("The stored document changed during verification.");
                    return true;
                }
                if (requiresValidation && !await ReadAndValidateAsync()) continue;
                await using var db = await factory.CreateDbContextAsync(token);
                var source = await db.Results.AsNoTracking().Where(x => x.DocumentId == file.DocumentId && x.FileHash == validation.Sha256 &&
                    x.DownloadStatus == DocumentDownloadStatus.Downloaded).OrderByDescending(x => x.Id)
                    .Select(x => x.ResponseJson).FirstOrDefaultAsync(token);
                var parsed = source is null ? null : ExtractionJobs.Deserialize<ReceivedDocumentResponse>(source);
                response.Validation = validation;
                response.Storage = reference with { Revision = inspection.Revision };
                response.FilePath = file.LocalPath;
                response.XmlHash = validation.Sha256;
                response.ReusedFileId = file.Id;
                if (query.DownloadFormat == DownloadFormat.Xml)
                {
                    var conversion = await CompatibleConversionAsync(db, file.DocumentId, validation.Sha256!, token);
                    // Fetch stored bytes only if either interpretation cannot be reused.
                    var hasParsedDocument = parsed?.ParseStatus == DocumentParseStatus.Parsed && parsed.ParsedDocument is not null;
                    var guide = query.DocumentType is DocumentType.RemissionGuide or DocumentType.RemissionGuideAlternative;
                    if ((!hasParsedDocument || conversion is null) && !guide && !await ReadAndValidateAsync()) continue;
                    response.Validation = validation;
                    response.XmlHash = validation.Sha256;
                    // Validation may have rebound the evidence to different bytes. Never reuse the old conversion in that case.
                    if (conversion?.XmlHash != validation.Sha256) conversion = await CompatibleConversionAsync(db, file.DocumentId, validation.Sha256!, token);
                    if (content is not null)
                    {
                        content.Stream.Position = 0;
                        using var reader = new StreamReader(content.Stream, leaveOpen: true);
                        response.SourceXml = await reader.ReadToEndAsync(token);
                        content.Stream.Position = 0;
                    }
                    if (hasParsedDocument && parsed!.Validation.Sha256 == validation.Sha256)
                    { response.ParseStatus = parsed.ParseStatus; response.ParsedDocument = parsed.ParsedDocument; }
                    else if (guide)
                    { response.ParseStatus = DocumentParseStatus.Unsupported; response.Errors.Add(new(ExtractionErrorCode.UnsupportedDocumentType, "Interpretation of this document type is not supported yet.", row, page)); }
                    else
                    {
                        DocumentParseResult interpretation;
                        try { interpretation = await documentParser.ParseAsync(content!, query.DocumentType).WaitAsync(token); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception) { interpretation = new(DocumentParseStatus.Failed, Error: new(ExtractionErrorCode.ParsingFailed, "Stored document content could not be interpreted.")); }
                        response.ParseStatus = interpretation.Status; response.ParsedDocument = interpretation.Document;
                        if (interpretation.Error is not null) response.Errors.Add(interpretation.Error with { PageNumber = page, RowIndex = row });
                    }
                }
                response.StorageStatus = DocumentStorageStatus.Stored;
                response.DownloadStatus = DocumentDownloadStatus.Downloaded;
                return response;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or DocumentSizeLimitException) { continue; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException)
            { return VerificationFailure(response); }
            finally { if (content is not null) await content.DisposeAsync(); }
        }
        return null;
    }

    internal static DocumentType CanonicalType(DocumentType type) => type == DocumentType.RemissionGuideAlternative ? DocumentType.RemissionGuide : type;
    internal static Task<DocumentConversion?> CompatibleConversionAsync(CrawlerDbContext db, long documentId, string hash, CancellationToken token) =>
        db.Conversions.AsNoTracking().Where(x => x.DocumentId == documentId && x.XmlHash == hash && x.Status == DocumentParseStatus.Parsed &&
            x.ParserName == HttpSriDocumentJsonParser.ParserName && x.ParserVersion == HttpSriDocumentJsonParser.ParserVersion &&
            x.SchemaVersion == JsonConversion.SchemaVersion && x.DocumentJson != null).OrderByDescending(x => x.Id).FirstOrDefaultAsync(token);

    private static ReceivedDocumentResponse VerificationFailure(ReceivedDocumentResponse response)
    {
        response.AcquisitionSource = DocumentAcquisitionSource.NotAcquired;
        response.Errors.Add(new(ExtractionErrorCode.StorageVerificationFailed, "Stored document availability could not be verified.", response.RowIndex, response.PageNumber));
        return response;
    }
}
