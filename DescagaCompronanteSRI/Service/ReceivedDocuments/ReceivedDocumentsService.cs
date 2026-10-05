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
    ILogger<ReceivedDocumentsService> logger) : IReceivedDocumentsService
{
    public async Task<ReceivedDocumentsResponse> QueryAsync(ReceivedDocumentsQuery query)
    {
        var result = new ReceivedDocumentsResponse { CompanyId = query.CompanyId, TaxpayerId = query.User };
        var stage = ExtractionErrorCode.LoginFailed;
        try
        {
            await using var session = sessionFactory.Create();
            var profile = await session.LoginAsync(query);
            if (profile is null)
                return Fail(result, stage, "SRI login failed.");
            result.BusinessName = profile.BusinessName;

            stage = ExtractionErrorCode.PortalAccessFailed;
            if (!await page.OpenAsync(session))
                return Fail(result, stage, "The received documents portal could not be opened.");

            stage = ExtractionErrorCode.QueryFailed;
            var queryResult = await page.QueryAsync(session, query);
            if (!queryResult.IsSuccess)
                return Fail(result, queryResult.Error!.Code, queryResult.Error.Message);

            result.QuerySucceeded = true;
            result.DiscoveredCount = queryResult.Value;
            stage = ExtractionErrorCode.UnexpectedError;
            if (result.DiscoveredCount == 0)
            {
                result.Status = ExtractionStatus.NoDocuments;
                return result;
            }

            for (var index = 0; index < result.DiscoveredCount; index++)
            {
                var document = await ProcessDocumentAsync(session, query, index);
                result.Documents.Add(document);
                logger.LogInformation("Received document {Row}/{Count}: {Status}, parse {ParseStatus}.",
                    index + 1, result.DiscoveredCount, document.DownloadStatus, document.ParseStatus);
            }
            result.Status = result.DownloadedCount == 0 ? ExtractionStatus.Failed
                : result.FailedCount > 0 ? ExtractionStatus.Partial : ExtractionStatus.Completed;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Received extraction failed during {Stage}.", stage);
            result.QuerySucceeded = false;
            return Fail(result, stage, "Received document extraction could not be executed.");
        }
        return result;
    }

    private async Task<ReceivedDocumentResponse> ProcessDocumentAsync(
        IReceivedDocumentsSession session, ReceivedDocumentsQuery query, int rowIndex)
    {
        var result = new ReceivedDocumentResponse { RowIndex = rowIndex, DownloadFormat = query.DownloadFormat };
        var stage = ExtractionErrorCode.RowReadFailed;
        try
        {
            var row = await page.ReadRowAsync(session, rowIndex);
            if (!row.IsSuccess)
            {
                result.Errors.Add(row.Error! with { RowIndex = rowIndex });
                return result;
            }
            result.Metadata = row.Value!.Metadata;
            stage = ExtractionErrorCode.DownloadFailed;
            var download = await downloader.DownloadAsync(session, row.Value, query.DownloadFormat);
            if (!download.IsSuccess)
            {
                result.Errors.Add(download.Error! with { RowIndex = rowIndex });
                return result;
            }

            await using var content = download.Value!;
            stage = ExtractionErrorCode.ParsingFailed;
            DocumentParseResult parsed;
            try
            {
                parsed = await parser.ParseAsync(content, query.DocumentType);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Document interpretation failed at row {RowIndex}.", rowIndex);
                parsed = new(DocumentParseStatus.Failed, Error: new(stage, "Document content could not be interpreted."));
            }
            result.ParseStatus = parsed.Status;
            result.ParsedDocument = parsed.Document;
            if (parsed.Error is not null) result.Errors.Add(parsed.Error with { RowIndex = rowIndex });

            stage = ExtractionErrorCode.StorageFailed;
            result.Storage = await storage.SaveAsync(new DocumentStorageContext(
                query.CompanyId, query.User, query.Year, query.Month, DocumentDirection.Received, query.DocumentType,
                result.Metadata.AuthorizationNumber), content);
            result.FilePath = result.Storage.LocalPath;
            result.DownloadStatus = DocumentDownloadStatus.Downloaded;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Document processing failed at row {RowIndex} during {Stage}.", rowIndex, stage);
            result.Errors.Add(new(stage, "Document processing failed.", rowIndex));
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
