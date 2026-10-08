using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Jobs;
using Microsoft.Extensions.Options;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Models.Storage;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class ReceivedDocumentsService(
    IReceivedDocumentsSessionFactory sessionFactory, IReceivedDocumentsPage page,
    IDocumentDownloader downloader, IDocumentParser parser, IDocumentStorage storage,
    ILogger<ReceivedDocumentsService> logger, IOptions<ReceivedDocumentsPaginationOptions> options, IDocumentValidator validator) : IReceivedDocumentsService, IReceivedExtractionRunner
{
    public Task<ReceivedDocumentsResponse> QueryAsync(ReceivedDocumentsQuery query) => ExecuteAsync(query, null, CancellationToken.None);
    public Task<ReceivedDocumentsResponse> RunAsync(ReceivedDocumentsQuery query, IExtractionProgress progress, CancellationToken token) => ExecuteAsync(query, progress, token);
    private async Task<ReceivedDocumentsResponse> ExecuteAsync(ReceivedDocumentsQuery query, IExtractionProgress? progress, CancellationToken token)
    {
        var result = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User, AccumulateDocuments = progress is null };
        var stage = ExtractionErrorCode.LoginFailed;
        try
        {
            await using var session = sessionFactory.Create();
            if (progress is not null) await progress.StageAsync(ExtractionStage.Login, token);
            var profile = await session.LoginAsync(query).WaitAsync(token);
            if (profile is null)
                return Fail(result, stage, "SRI login failed.");
            result.BusinessName = profile.BusinessName;

            stage = ExtractionErrorCode.PortalAccessFailed;
            if (progress is not null) await progress.StageAsync(ExtractionStage.PortalAccess, token);
            if (!await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.PortalAccess, () => page.OpenAsync(session).WaitAsync(token),
                ok => ok ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed))
                return Fail(result, stage, "The received documents portal could not be opened.");

            stage = ExtractionErrorCode.QueryFailed;
            if (progress is not null) await progress.StageAsync(ExtractionStage.Query, token);
            var queryResult = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Query, () => page.QueryAsync(session, query).WaitAsync(token),
                value => value.IsSuccess ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed);
            if (!queryResult.IsSuccess)
                return Fail(result, queryResult.Error!.Code, queryResult.Error.Message);

            result.QuerySucceeded = true;
            result.Pagination.Status = PaginationStatus.Incomplete;
            stage = ExtractionErrorCode.PaginationNavigationFailed;
            if (progress is not null) await progress.StageAsync(ExtractionStage.Processing, token);
            if (progress is not null) await progress.CheckpointAsync(result, token);
            await TraverseAsync(session, query, queryResult.Value!, result, progress, token);
            SetStatus(result);
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception) when (progress is not null) { throw; }
        catch (Exception exception)
        {
            ExtractionDiagnostics.Event(logger, LogLevel.Error, DiagnosticEvent.AttemptInterrupted, code: stage.ToString(), errorType: exception.GetType().Name);
            if (!result.QuerySucceeded)
                return Fail(result, stage, "Received document extraction could not be executed.");
            result.Pagination.Status = PaginationStatus.Incomplete;
            result.Errors.Add(new(stage, "The document traversal was interrupted.",
                PageNumber: result.Pagination.PagesProcessed + 1));
            SetStatus(result);
        }
        if (progress is not null) await progress.CheckpointAsync(result, token);
        return result;
    }

    private async Task TraverseAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query,
        ReceivedDocumentsPageSnapshot snapshot, ReceivedDocumentsResponse result, IExtractionProgress? progress, CancellationToken token)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var expectedPage = 1;
        var rowsVisited = 0;
        var consistent = true;
        void Error(ExtractionErrorCode code, string message, int? row = null)
        {
            consistent = false;
            result.Errors.Add(new(code, message, row, expectedPage));
            ExtractionDiagnostics.Safely(() => logger.LogWarning("Received pagination page {Page}: {Reason}", expectedPage, message));
        }

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.PageNumber is not null && snapshot.PageNumber != expectedPage)
            {
                Error(ExtractionErrorCode.PaginationInconsistent, "The portal skipped or moved backwards from the expected page.");
                return;
            }
            if (snapshot.Rows.Count > 0 && !fingerprints.Add(snapshot.Fingerprint))
            {
                Error(ExtractionErrorCode.RepeatedPage, "The portal returned a previously processed page.");
                return;
            }
            if (snapshot.ReportedTotalCount is int total)
            {
                if (result.Pagination.ReportedTotalCount is int previous && previous != total)
                    Error(ExtractionErrorCode.PaginationInconsistent, "The portal total changed during extraction.");
                result.Pagination.ReportedTotalCount ??= total;
            }
            if (snapshot.StateError is not null)
                Error(snapshot.StateError.Code, snapshot.StateError.Message);

            if (snapshot.Rows.Count == 0)
            {
                if (expectedPage == 1 && snapshot.IsEmptyConfirmed && snapshot.HasNextPage == false &&
                    (result.Pagination.ReportedTotalCount is null or 0) && consistent)
                    result.Pagination.Status = PaginationStatus.Completed;
                else Error(ExtractionErrorCode.PaginationInconsistent, "An unexpected empty page prevented verification of the results.");
                return;
            }

            result.Pagination.PagesProcessed++;
            rowsVisited += snapshot.Rows.Count;
            if (progress is not null) await progress.PreparePageAsync(snapshot.Rows.Select(x => x.Result.Value?.Metadata.AuthorizationNumber)
                .Where(x => x is { Length: 49 } && x.All(char.IsAsciiDigit)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray(), token);
            foreach (var row in snapshot.Rows)
            {
                using var documentScope = ExtractionDiagnostics.DocumentScope(logger, expectedPage, row.RowIndex, ExtractionDiagnostics.Name(query.DownloadFormat));
                var key = row.Result.Value?.Metadata.AuthorizationNumber;
                if (key is { Length: 49 } && key.All(char.IsAsciiDigit) && !keys.Add(key))
                {
                    result.Pagination.DuplicateCount++;
                    Error(ExtractionErrorCode.DuplicateDocument, "A document appeared more than once in this extraction.", row.RowIndex);
                    continue;
                }
                token.ThrowIfCancellationRequested();
                var document = key is { Length: 49 } && progress is not null
                    ? await progress.FindSavedAsync(row.Result.Value!.Metadata, expectedPage, row.RowIndex, token) : null;
                if (document is null)
                {
                    document = await ProcessDocumentAsync(session, query, row, expectedPage, token);
                    if (progress is not null) await progress.SaveAsync(document, token);
                }
                if (result.AccumulateDocuments) result.Documents.Add(document);
                result.ProcessedCount++;
                if (document.DownloadStatus == DocumentDownloadStatus.Downloaded)
                {
                    result.SavedCount++;
                    if (document.AcquisitionSource == DocumentAcquisitionSource.Reused) result.ReusedFiles++;
                }
                if (document.Validation.Status is DocumentValidationStatus.Invalid or DocumentValidationStatus.Unsupported or DocumentValidationStatus.Failed)
                    result.ValidationIssues++;
                if (document.MetadataParseStatus is MetadataParseStatus.Partial or MetadataParseStatus.Failed)
                    result.MetadataIssues++;
                foreach (var error in document.Errors)
                    ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.DocumentIssue, code: ExtractionDiagnostics.Name(error.Code),
                        stage: ExtractionStage.Processing, detail: document.Storage is null ? null : ExtractionDiagnostics.Name(document.Storage.Provider));
                document.SourceXml = null;
                if (progress is not null) await progress.CheckpointAsync(result, token);
                ExtractionDiagnostics.Safely(() => logger.LogDebug("Received page {Page}, row {Row}: {Status}, acquisition {AcquisitionSource}, parse {ParseStatus}, provider {provider}; {Count} results accumulated.",
                    expectedPage, row.RowIndex, document.DownloadStatus, ExtractionDiagnostics.Name(document.AcquisitionSource), ExtractionDiagnostics.Name(document.ParseStatus),
                    document.Storage is null ? null : ExtractionDiagnostics.Name(document.Storage.Provider), result.DiscoveredCount));
            }
            ExtractionDiagnostics.Event(logger, LogLevel.Information, DiagnosticEvent.PageProcessed, detail: expectedPage);
            ExtractionDiagnostics.Safely(() => logger.LogInformation("Received page {Page} processed: {Discovered} discovered, {Downloaded} available, {Reused} reused, {Failed} failed.",
                expectedPage, result.DiscoveredCount, result.DownloadedCount, result.ReusedCount, result.FailedCount));

            if (snapshot.PageNumber is null)
            {
                Error(ExtractionErrorCode.PaginationStateUnknown, "The current page position could not be verified.");
                return;
            }
            if (snapshot.HasNextPage == false)
            {
                if (result.Pagination.ReportedTotalCount is int announced && announced != rowsVisited)
                    Error(ExtractionErrorCode.PaginationInconsistent, "The traversed row count does not match the portal total.");
                if (consistent) result.Pagination.Status = PaginationStatus.Completed;
                return;
            }
            if (snapshot.HasNextPage is null || snapshot.StateError is not null)
            {
                Error(ExtractionErrorCode.PaginationStateUnknown, "The portal did not provide enough evidence to continue or confirm the end.");
                return;
            }
            if (result.Pagination.PagesProcessed >= options.Value.MaxPages)
            {
                Error(ExtractionErrorCode.PaginationLimitReached, "The configured page limit was reached with more pages pending.");
                return;
            }
            expectedPage++;
            var next = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Pagination, () => page.MoveNextAsync(session, snapshot).WaitAsync(token),
                value => value.IsSuccess ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed);
            if (!next.IsSuccess)
            {
                Error(next.Error!.Code, next.Error.Message);
                return;
            }
            snapshot = next.Value!;
        }
    }

    private static void SetStatus(ReceivedDocumentsResponse result)
    {
        var complete = result.Pagination.Status == PaginationStatus.Completed;
        result.Status = result.DownloadedCount > 0
            ? complete && result.FailedCount == 0 ? ExtractionStatus.Completed : ExtractionStatus.Partial
            : complete && result.DiscoveredCount == 0 ? ExtractionStatus.NoDocuments : ExtractionStatus.Failed;
    }

    private async Task<ReceivedDocumentResponse> ProcessDocumentAsync(
        IReceivedDocumentsSession session, ReceivedDocumentsQuery query, ReceivedDocumentRowSnapshot snapshot, int pageNumber, CancellationToken token)
    {
        var rowIndex = snapshot.RowIndex;
        var result = new ReceivedDocumentResponse { PageNumber = pageNumber, RowIndex = rowIndex, DownloadFormat = query.DownloadFormat };
        var stage = ExtractionErrorCode.RowReadFailed;
        try
        {
            var row = snapshot.Result;
            if (!row.IsSuccess)
            {
                result.Errors.Add(row.Error! with { RowIndex = rowIndex, PageNumber = pageNumber });
                return result;
            }
            result.Metadata = row.Value!.Metadata;
            result.Errors.AddRange(result.Metadata.Errors.Select(error => error with { RowIndex = rowIndex, PageNumber = pageNumber }));
            stage = ExtractionErrorCode.DownloadFailed;
            var download = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Download, () => downloader.DownloadAsync(session, row.Value, query.DownloadFormat, token),
                value => value.IsSuccess ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed);
            if (!download.IsSuccess)
            {
                var error = download.Error! with { RowIndex = rowIndex, PageNumber = pageNumber };
                result.Errors.Add(error);
                if (error.Code is ExtractionErrorCode.InvalidDocument or ExtractionErrorCode.InputTooLarge)
                    result.Validation = new() { Status = DocumentValidationStatus.Invalid, ValidatorVersion = validator.Version,
                        ValidatedAt = DateTimeOffset.UtcNow, Errors = [error] };
                return result;
            }

            await using var content = download.Value!;
            stage = ExtractionErrorCode.ValidationFailed;
            result.Validation = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.Validation,
                () => validator.ValidateAsync(content, query.DocumentType, result.Metadata.AuthorizationNumber, token),
                value => value.Status == DocumentValidationStatus.Valid ? DiagnosticOutcome.Succeeded :
                    value.Status == DocumentValidationStatus.Unsupported ? DiagnosticOutcome.Unsupported : DiagnosticOutcome.Failed);
            result.Errors.AddRange(result.Validation.Errors.Select(error => error with { RowIndex = rowIndex, PageNumber = pageNumber }));
            if (result.Validation.Status != DocumentValidationStatus.Valid) return result;
            result.SourceXml = content.SourceXml;
            if (content.Format == DownloadFormat.Xml)
            {
                result.XmlHash = result.Validation.Sha256;
                content.Stream.Position = 0;
                if (result.SourceXml is null)
                {
                    using var xmlReader = new StreamReader(content.Stream, leaveOpen: true);
                    result.SourceXml = await xmlReader.ReadToEndAsync(token);
                    content.Stream.Position = 0;
                }
            }
            stage = ExtractionErrorCode.StorageFailed;
            result.StorageStatus = DocumentStorageStatus.Failed;
            result.Storage = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.StorageWrite, () => storage.SaveAsync(new DocumentStorageContext(
                query.CompanyId, query.User, query.Year, query.Month, DocumentDirection.Received, query.DocumentType,
                result.Metadata.AuthorizationNumber), content, token));
            result.StorageStatus = DocumentStorageStatus.Stored;
            result.FilePath = result.Storage.LocalPath;
            result.DownloadStatus = DocumentDownloadStatus.Downloaded;
            result.AcquisitionSource = DocumentAcquisitionSource.Downloaded;
            content.Stream.Position = 0;
            stage = ExtractionErrorCode.ParsingFailed;
            DocumentParseResult parsed;
            try
            {
                parsed = await ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.LegacyParsing, () => parser.ParseAsync(content, query.DocumentType).WaitAsync(token),
                    value => value.Status == DocumentParseStatus.Failed ? DiagnosticOutcome.Failed :
                        value.Status == DocumentParseStatus.Unsupported ? DiagnosticOutcome.Unsupported : DiagnosticOutcome.Succeeded);
            }
            catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            catch (Exception exception)
            {
                ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.OperationFinished, code: "parsingFailed", errorType: exception.GetType().Name);
                parsed = new(DocumentParseStatus.Failed, Error: new(stage, "Document content could not be interpreted."));
            }
            result.ParseStatus = parsed.Status;
            result.ParsedDocument = parsed.Document;
            if (parsed.Error is not null) result.Errors.Add(parsed.Error with { RowIndex = rowIndex, PageNumber = pageNumber });
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception exception)
        {
            ExtractionDiagnostics.Event(logger, LogLevel.Warning, DiagnosticEvent.OperationFinished, code: stage.ToString(), errorType: exception.GetType().Name);
            var error = new ExtractionError(stage, "Document processing failed.", rowIndex, pageNumber);
            result.Errors.Add(error);
            if (stage == ExtractionErrorCode.ValidationFailed)
                result.Validation = new() { Status = DocumentValidationStatus.Failed, ValidatorVersion = validator.Version,
                    ValidatedAt = DateTimeOffset.UtcNow, Errors = [error] };
        }
        return result;
    }

    private static ReceivedDocumentsResponse Fail(ReceivedDocumentsResponse result, ExtractionErrorCode code, string message)
    {
        result.Status = ExtractionStatus.Failed;
        result.Errors.Add(new(code, message));
        return result;
    }
}
