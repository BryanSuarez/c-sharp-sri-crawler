using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Responses;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DescagaCompronanteSRI.Jobs;

public sealed partial class ExtractionJobs
{
    public async Task<ExtractionAccepted?> RetryAsync(Guid sourceId, string companyId, SelectedRetryMode mode,
        IReadOnlyList<long> documentIds, string? password, Guid? clientRequestId, CancellationToken token)
    {
        if (!Enum.IsDefined(mode) || documentIds.Count is < 1 or > 500 || documentIds.Any(x => x <= 0) || documentIds.Distinct().Count() != documentIds.Count)
            throw new RetrySelectionException("invalidRetrySelection", "Select between 1 and 500 distinct document result identifiers.");
        if (mode == SelectedRetryMode.Download && string.IsNullOrWhiteSpace(password))
            throw new RetrySelectionException("retryCredentialsRequired", "A fresh SRI password is required for download retries.");
        if (mode == SelectedRetryMode.Parsing && !string.IsNullOrEmpty(password))
            throw new RetrySelectionException("retryCredentialsNotAllowed", "Parsing retries do not use SRI credentials.");
        try
        {
            var source = await Find(sourceId, companyId, token);
            if (source is null) return null;
            var query = Deserialize<ReceivedDocumentsQuery>(source.QueryJson) with
            { Password = password ?? "", DownloadPolicy = mode == SelectedRetryMode.Download ? DownloadPolicy.Refresh : DownloadPolicy.ReuseValid };
            return await AcceptCoreAsync(query, clientRequestId, token, sourceId, mode, documentIds.Order().ToArray());
        }
        catch (Exception e) when (e is NpgsqlException or DbUpdateException or InvalidOperationException { InnerException: NpgsqlException }) { throw new PersistenceUnavailableException(); }
    }

    private async Task<ExtractionRecord> ValidateSelectionAsync(Guid sourceId, string companyId, SelectedRetryMode mode, long[] ids, CancellationToken token)
    {
        var source = await Find(sourceId, companyId, token);
        if (source is null || !ExtractionLifecycle.IsTerminal(source.JobStatus))
            throw new RetrySelectionException("retrySourceNotFinished", "The source extraction must be finished before selecting failures.");
        var rows = await CurrentResults(source).Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Identity, x.DownloadStatus, x.FileId, x.JsonStatus }).ToListAsync(token);
        var format = Deserialize<ReceivedDocumentsQuery>(source.QueryJson).DownloadFormat;
        if (rows.Count != ids.Length)
            throw new RetrySelectionException("invalidRetrySelection", "Every selected result must belong to the source extraction.");
        foreach (var row in rows)
        {
            var key = row.Identity;
            var parsingFailed = row.JsonStatus == DocumentParseStatus.Failed;
            if (mode == SelectedRetryMode.Parsing && !parsingFailed && row.DownloadStatus == DocumentDownloadStatus.Downloaded && format == DownloadFormat.Xml)
            {
                // Only legacy parsing needs the historical response; load one document at a time.
                var json = await db.Results.Where(x => x.Id == row.Id && x.ExtractionId == sourceId).Select(x => x.ResponseJson).SingleAsync(token);
                parsingFailed = Deserialize<ReceivedDocumentResponse>(json).ParseStatus == DocumentParseStatus.Failed;
            }
            var eligible = mode == SelectedRetryMode.Download
                ? row.DownloadStatus == DocumentDownloadStatus.Failed
                : row.DownloadStatus == DocumentDownloadStatus.Downloaded && format == DownloadFormat.Xml && row.FileId != null &&
                    parsingFailed;
            if (!eligible || key is not { Length: 49 } || !key.All(char.IsAsciiDigit))
                throw new RetrySelectionException("resultNotRetryable", "Select failed downloads with a usable access key or stored XML results with failed parsing, according to the retry mode.");
        }
        return source;
    }
}
