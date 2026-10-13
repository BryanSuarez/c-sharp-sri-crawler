using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DescagaCompronanteSRI.Jobs;

public sealed partial class ExtractionWorker
{
    private async Task<ReceivedDocumentsResponse> RunParsingRetryAsync(ExtractionRecord record, long[] ids,
        PersistentExtractionProgress progress, CancellationToken token)
    {
        var response = new ReceivedDocumentsResponse { CompanyId = record.CompanyId, TaxpayerId = record.TaxpayerId,
            BusinessName = record.BusinessName, QuerySucceeded = true, AccumulateDocuments = false };
        await progress.StageAsync(ExtractionStage.Processing, token);
        var legacyParsingFailed = false;
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested();
            await using var db = await factory.CreateDbContextAsync(token);
            var source = await db.Results.AsNoTracking().SingleAsync(x => x.ExtractionId == record.SourceExtractionId && x.Id == id, token);
            var original = ExtractionJobs.Deserialize<ReceivedDocumentResponse>(source.ResponseJson);
            await progress.PreparePageAsync([source.Identity], token);
            // Recovery of this child has priority; confirmed bytes and conversions may be reused.
            var result = await progress.ReprocessSavedAsync(source, token);
            if (result is null)
            {
                result = new ReceivedDocumentResponse { Metadata = original.Metadata, PageNumber = original.PageNumber,
                    RowIndex = original.RowIndex, DownloadFormat = DownloadFormat.Xml };
                result.Errors.Add(new(ExtractionErrorCode.StoredRetryFileUnavailable,
                    "No readable, validated XML is available for parsing. Request a download separately.", original.RowIndex, original.PageNumber));
                await progress.SaveAsync(result, token);
            }
            legacyParsingFailed |= result.ParseStatus == DocumentParseStatus.Failed;
            response.ProcessedCount++;
            if (result.DownloadStatus == DocumentDownloadStatus.Downloaded) { response.SavedCount++; response.ReusedFiles++; }
            result.SourceXml = null;
            await progress.CheckpointAsync(response, token);
        }
        await using var completion = await factory.CreateDbContextAsync(token);
        var jsonFailed = await completion.Results.AnyAsync(x => x.ExtractionId == record.Id && x.JsonStatus != DocumentParseStatus.Parsed, token);
        response.Status = response.FailedCount == 0 && !legacyParsingFailed && !jsonFailed ? ExtractionStatus.Completed : response.SavedCount > 0 ? ExtractionStatus.Partial : ExtractionStatus.Failed;
        return response;
    }

    private async Task RecordUnseenSelectionsAsync(ExtractionRecord record, long[] ids, PersistentExtractionProgress progress,
        ReceivedDocumentsResponse response, CancellationToken token)
    {
        foreach (var id in ids)
        {
            await using var db = await factory.CreateDbContextAsync(token);
            var source = await db.Results.AsNoTracking().SingleAsync(x => x.ExtractionId == record.SourceExtractionId && x.Id == id, token);
            if (await db.Results.AnyAsync(x => x.ExtractionId == record.Id && x.Identity == source.Identity && (x.SeenAttemptId == record.ActiveAttemptId || x.DownloadStatus == DocumentDownloadStatus.Downloaded), token)) continue;
            var original = ExtractionJobs.Deserialize<ReceivedDocumentResponse>(source.ResponseJson);
            var missing = new ReceivedDocumentResponse { Metadata = original.Metadata, PageNumber = original.PageNumber,
                RowIndex = original.RowIndex, DownloadFormat = original.DownloadFormat };
            missing.Errors.Add(new(response.Pagination.Status == PaginationStatus.Completed ? ExtractionErrorCode.SelectedDocumentNotFound : ExtractionErrorCode.SelectedDocumentNotReached,
                "The selected document was not observed in the current portal traversal; location refers to the source extraction.", original.RowIndex, original.PageNumber));
            await progress.SaveAsync(missing, token);
            response.ProcessedCount++;
            response.Status = response.DownloadedCount > 0 ? ExtractionStatus.Partial : ExtractionStatus.Failed;
        }
    }
}
