using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Jobs;
using System.Text;
using Microsoft.Extensions.Options;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Models.Storage;
using DescagaCompronanteSRI.Services.Storage;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public class ReceivedDocumentsServiceTests
{
    [Theory]
    [InlineData(DownloadFormat.Xml, DocumentParseStatus.Parsed)]
    [InlineData(DownloadFormat.Pdf, DocumentParseStatus.NotApplicable)]
    public async Task Query_ProcessesAndDisposesContent(DownloadFormat format, DocumentParseStatus parseStatus)
    {
        var scenario = new Scenario();
        var response = await scenario.Service.QueryAsync(Query(format));
        Assert.Equal(ExtractionStatus.Completed, response.Status);
        Assert.True(response.QuerySucceeded);
        Assert.Equal(1, response.DiscoveredCount);
        Assert.Equal(1, response.DownloadedCount);
        Assert.Equal(0, response.FailedCount);
        var document = Assert.Single(response.Documents);
        Assert.Equal(parseStatus, document.ParseStatus);
        Assert.Equal("1790012345001", response.TaxpayerId);
        Assert.Equal("Test Company", response.BusinessName);
        Assert.Equal("acme", response.CompanyId);
        Assert.StartsWith("acme/1790012345001/2026/06/received/invoice/", document.Storage!.Key);
        Assert.Equal("/test/file", document.FilePath);
        Assert.True(scenario.Session.Disposed);
        Assert.All(scenario.Contents, content => Assert.False(content.Stream.CanRead));
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task Query_NoDocumentsDoesNotDownload()
    {
        var scenario = new Scenario { RowCount = 0 };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.NoDocuments, response.Status);
        Assert.True(response.QuerySucceeded);
        Assert.Empty(response.Documents);
        Assert.Equal(0, scenario.DownloadCount);
        Assert.True(scenario.Session.Disposed);
    }

    [Fact]
    public async Task Query_PartialDownloadContinuesToNextRow()
    {
        var scenario = new Scenario { RowCount = 3, FailedDownloadRow = 1 };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, response.Status);
        Assert.Equal(2, response.DownloadedCount);
        Assert.Equal(1, response.FailedCount);
        Assert.Equal(ExtractionErrorCode.DownloadFailed, response.Documents[1].Errors.Single().Code);
        Assert.Equal(2, response.Documents[2].RowIndex);
    }

    [Fact]
    public async Task Query_AllDownloadsFailedStillRepresentsAnExecutedQuery()
    {
        var scenario = new Scenario { FailedDownloadRow = 0 };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Failed, response.Status);
        Assert.True(response.QuerySucceeded);
        Assert.Equal(1, response.FailedCount);
    }

    [Fact]
    public async Task Query_UnreadableRowIsCountedAndDoesNotStopExtraction()
    {
        var scenario = new Scenario { RowCount = 2, FailedReadRow = 0 };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, response.Status);
        Assert.Equal(1, response.FailedCount);
        Assert.Null(response.Documents[0].Metadata);
        Assert.Equal(ExtractionErrorCode.RowReadFailed, response.Documents[0].Errors.Single().Code);
    }

    [Fact]
    public async Task Query_StorageExceptionIsReportedAndContentDisposed()
    {
        var scenario = new Scenario { ThrowOnSave = true };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(0, response.DownloadedCount);
        Assert.Equal(1, response.FailedCount);
        Assert.Equal(ExtractionErrorCode.StorageFailed, response.Documents[0].Errors.Single().Code);
        Assert.All(scenario.Contents, content => Assert.False(content.Stream.CanRead));
        Assert.True(scenario.Session.Disposed);
    }

    [Fact]
    public async Task Query_ParsingExceptionKeepsDownloadedFileAndReportsInterpretationFailure()
    {
        var scenario = new Scenario { ThrowOnParse = true };
        var response = await scenario.Service.QueryAsync(Query());
        var document = Assert.Single(response.Documents);
        Assert.Equal(DocumentDownloadStatus.Downloaded, document.DownloadStatus);
        Assert.Equal(DocumentParseStatus.Failed, document.ParseStatus);
        Assert.Equal(ExtractionErrorCode.ParsingFailed, document.Errors.Single().Code);
        Assert.Equal(1, scenario.SaveCount);
    }

    [Fact]
    public async Task Query_UnsupportedInterpretationStillSavesOriginal()
    {
        var scenario = new Scenario();
        var response = await scenario.Service.QueryAsync(Query() with { DocumentType = DocumentType.RemissionGuide });
        Assert.Equal(DocumentParseStatus.Unsupported, response.Documents[0].ParseStatus);
        Assert.Equal(DocumentDownloadStatus.Downloaded, response.Documents[0].DownloadStatus);
        Assert.Equal(ExtractionErrorCode.UnsupportedDocumentType, response.Documents[0].Errors.Single().Code);
    }

    [Theory]
    [InlineData("login", ExtractionErrorCode.LoginFailed)]
    [InlineData("portal", ExtractionErrorCode.PortalAccessFailed)]
    [InlineData("query", ExtractionErrorCode.QueryFailed)]
    public async Task Query_StageFailureDisposesSession(string stage, ExtractionErrorCode expected)
    {
        var scenario = new Scenario { FailureStage = stage };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.False(response.QuerySucceeded);
        Assert.Equal(ExtractionStatus.Failed, response.Status);
        Assert.Equal(expected, Assert.Single(response.Errors).Code);
        Assert.True(scenario.Session.Disposed);
        Assert.Empty(response.Documents);
    }

    [Fact]
    public async Task Query_UnexpectedPortalExceptionIsControlled()
    {
        var scenario = new Scenario { ThrowOnOpen = true };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.False(response.QuerySucceeded);
        Assert.True(scenario.Session.Disposed);
        Assert.DoesNotContain("secret", Assert.Single(response.Errors).Message);
        Assert.Equal(ExtractionErrorCode.PortalAccessFailed, response.Errors[0].Code);
    }

    [Theory]
    [InlineData(StorageProvider.S3)]
    [InlineData(StorageProvider.R2)]
    public async Task Query_RemoteStorageReturnsReferenceWithoutLocalPath(StorageProvider provider)
    {
        var scenario = new Scenario { Provider = provider };
        var response = await scenario.Service.QueryAsync(Query());
        var document = Assert.Single(response.Documents);
        Assert.Equal(provider, document.Storage!.Provider);
        Assert.Equal("test-bucket", document.Storage.Bucket);
        Assert.Null(document.FilePath);
        Assert.Equal(DocumentDownloadStatus.Downloaded, document.DownloadStatus);
    }

    [Fact]
    public async Task Query_StorageFailureContinuesWithRemainingDocuments()
    {
        var scenario = new Scenario { RowCount = 3, FailedSaveRow = 1 };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, response.Status);
        Assert.Equal(2, response.DownloadedCount);
        Assert.Null(response.Documents[1].Storage);
        Assert.Equal(ExtractionErrorCode.StorageFailed, response.Documents[1].Errors.Single().Code);
        Assert.Equal(DocumentDownloadStatus.Downloaded, response.Documents[2].DownloadStatus);
    }

    [Fact]
    public async Task Query_InvalidAccessKeyIsRejectedBeforeStorage()
    {
        var response = await new Scenario { InvalidAccessKey = true }.Service.QueryAsync(Query());
        Assert.Equal(0, response.DownloadedCount);
        Assert.Null(response.Documents[0].Storage);
        Assert.Equal(ExtractionErrorCode.InvalidDocument, response.Documents[0].Errors.Single().Code);
    }

    [Fact]
    public async Task Query_StoresUnderRequestedYearAndMonth()
    {
        var scenario = new Scenario();
        var response = await scenario.Service.QueryAsync(Query() with { Year = 2025, Month = 1 });
        Assert.Equal(2025, scenario.LastStorageContext!.Year);
        Assert.Equal(1, scenario.LastStorageContext.Month);
        Assert.StartsWith("acme/1790012345001/2025/01/received/invoice/", response.Documents[0].Storage!.Key);
    }

    [Fact]
    public async Task Query_RejectsInvalidContentBeforeStorageOrParsing()
    {
        var scenario = new Scenario { InvalidContent = true, ThrowOnParse = true };
        var response = await scenario.Service.QueryAsync(Query());
        var document = Assert.Single(response.Documents);
        Assert.Equal(DocumentValidationStatus.Invalid, document.Validation.Status);
        Assert.Equal(DocumentStorageStatus.NotAttempted, document.StorageStatus);
        Assert.Equal(DocumentParseStatus.NotApplicable, document.ParseStatus);
        Assert.Equal(0, scenario.SaveCount);
        Assert.Equal(1, response.ValidationIssueCount);
        Assert.True(scenario.Session.Disposed);
        Assert.All(scenario.Contents, content => Assert.False(content.Stream.CanRead));
    }
    [Fact]
    public async Task Query_MetadataIssueDoesNotPreventValidDownload()
    {
        var scenario = new Scenario { MetadataIssue = true };
        var response = await scenario.Service.QueryAsync(Query());
        var document = Assert.Single(response.Documents);
        Assert.Equal(1, response.DownloadedCount);
        Assert.Equal(1, response.MetadataIssueCount);
        Assert.Equal(MetadataParseStatus.Partial, document.MetadataParseStatus);
        Assert.Equal("amount", Assert.Single(document.Errors).Field);
        Assert.Equal(1, document.Errors[0].PageNumber);
    }

    [Fact]
    public async Task Query_RejectedReplacementPreservesPreviouslyValidLocalFile()
    {
        using var environment = new Storage.StorageTestEnvironment();
        var storage = new LocalDocumentStorage(environment);
        var xml = Validation.DocumentValidatorTests.Fixture();
        var key = Validation.DocumentValidatorTests.AccessKey(xml);
        var validator = Validation.DocumentValidatorTests.Validator();
        await using var original = new DocumentContent(new MemoryStream(Encoding.UTF8.GetBytes(xml)), DownloadFormat.Xml);
        Assert.Equal(DocumentValidationStatus.Valid, (await validator.ValidateAsync(original, DocumentType.Invoice, key)).Status);
        var saved = await storage.SaveAsync(new("acme", "1790012345001", 2026, 6, DocumentDirection.Received, DocumentType.Invoice, key), original);
        var row = new ReceivedDocumentRowSnapshot(0, OperationResult<ReceivedDocumentReference>.Success(
            new(new ReceivedDocumentMetadata { AuthorizationNumber = key }, "xml", "pdf", "detail")), "key:" + key);
        var scenario = new Scenario { StorageOverride = storage, ValidatorOverride = validator,
            Pages = [new(1, false, 1, [row])] };
        var response = await scenario.Service.QueryAsync(Query());
        Assert.Equal(DocumentDownloadStatus.Failed, response.Documents[0].DownloadStatus);
        Assert.Equal(DocumentStorageStatus.NotAttempted, response.Documents[0].StorageStatus);
        Assert.Equal(Encoding.UTF8.GetBytes(xml), await File.ReadAllBytesAsync(saved.LocalPath!));
        Assert.True(scenario.Session.Disposed);
    }

    private static ReceivedDocumentsPageSnapshot Snapshot(int number, bool? next, int? total, params int[] keys) =>
        new(number, next, total, keys.Select((key, index) => new ReceivedDocumentRowSnapshot(index,
            OperationResult<ReceivedDocumentReference>.Success(new(
                new ReceivedDocumentMetadata { AuthorizationNumber = key.ToString().PadLeft(49, '0') },
                $"xml-{number}-{index}", $"pdf-{number}-{index}", "detail")), $"key:{key}")).ToArray());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Query_TraversesFullAndPartialFinalPages(int lastSize)
    {
        var scenario = new Scenario { Pages = [Snapshot(1, true, 2 + lastSize, 1, 2),
            Snapshot(2, false, 2 + lastSize, Enumerable.Range(3, lastSize).ToArray())] };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Completed, result.Status);
        Assert.Equal(PaginationStatus.Completed, result.Pagination.Status);
        Assert.Equal(2, result.Pagination.PagesProcessed);
        Assert.Equal(2 + lastSize, result.DownloadedCount);
        Assert.Equal(2, result.Documents[2].PageNumber);
        Assert.Equal(0, result.Documents[2].RowIndex);
        Assert.True(scenario.Session.Disposed);
    }

    [Fact]
    public async Task Query_OverlappingDocumentsAreSkippedButNewDocumentsAreSaved()
    {
        var scenario = new Scenario { Pages = [Snapshot(1, true, 4, 1, 2), Snapshot(2, false, 4, 2, 3)] };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Equal(PaginationStatus.Incomplete, result.Pagination.Status);
        Assert.Equal(1, result.Pagination.DuplicateCount);
        Assert.Equal(3, result.DiscoveredCount);
        Assert.Equal(3, scenario.SaveCount);
        Assert.Contains(result.Errors, e => e.Code == ExtractionErrorCode.DuplicateDocument && e.PageNumber == 2 && e.RowIndex == 0);
    }

    [Theory]
    [InlineData(2, ExtractionErrorCode.RepeatedPage)]
    [InlineData(1, ExtractionErrorCode.PaginationInconsistent)]
    [InlineData(3, ExtractionErrorCode.PaginationInconsistent)]
    public async Task Query_RepeatedSkippedOrBackwardPagesStop(int secondPage, ExtractionErrorCode code)
    {
        var scenario = new Scenario { Pages = [Snapshot(1, true, 4, 1, 2), Snapshot(secondPage, true, 4, 2, 1)] };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Equal(1, result.Pagination.PagesProcessed);
        Assert.Equal(2, scenario.SaveCount);
        Assert.Contains(result.Errors, e => e.Code == code);
    }

    [Theory]
    [InlineData(5, 4, false)]
    [InlineData(4, 5, false)]
    [InlineData(4, 4, true)]
    public async Task Query_ChangingTotalsPrematureEndOrEmptyPageAreIncomplete(int firstTotal, int secondTotal, bool empty)
    {
        var result = await new Scenario { Pages = [Snapshot(1, true, firstTotal, 1, 2),
            Snapshot(2, false, secondTotal, empty ? [] : [3, 4])] }.Service.QueryAsync(Query());
        Assert.Equal(PaginationStatus.Incomplete, result.Pagination.Status);
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Contains(result.Errors, e => e.Code == ExtractionErrorCode.PaginationInconsistent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_NavigationFailurePreservesResultsAndDisposesSession(bool throws)
    {
        var scenario = new Scenario { Pages = [Snapshot(1, true, 2, 1)], FailNavigation = !throws, ThrowOnNavigation = throws };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.True(result.QuerySucceeded);
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Equal(PaginationStatus.Incomplete, result.Pagination.Status);
        Assert.Single(result.Documents);
        Assert.NotNull(result.Documents[0].Storage);
        Assert.True(scenario.Session.Disposed);
        Assert.Contains(result.Errors, e => e.PageNumber == 2);
        Assert.All(scenario.Contents, c => Assert.False(c.Stream.CanRead));
    }

    [Fact]
    public async Task Query_UnknownPaginationCannotClaimCompletion()
    {
        var result = await new Scenario { Pages = [Snapshot(1, null, null, 1)] }.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Contains(result.Errors, e => e.Code == ExtractionErrorCode.PaginationStateUnknown);
    }

    [Theory]
    [InlineData(true, PaginationStatus.Incomplete)]
    [InlineData(false, PaginationStatus.Completed)]
    public async Task Query_PageLimitOnlyFailsWhenPagesRemain(bool next, PaginationStatus status)
    {
        var scenario = new Scenario { MaxPages = 1, Pages = [Snapshot(1, next, null, 1)] };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.Equal(status, result.Pagination.Status);
        Assert.Equal(0, scenario.Moves);
        Assert.Equal(next, result.Errors.Any(e => e.Code == ExtractionErrorCode.PaginationLimitReached));
    }

    [Fact]
    public async Task Query_DuplicateKeepsTheFirstFailureInsteadOfDownloadingItAgain()
    {
        var scenario = new Scenario { Pages = [Snapshot(1, true, 3, 1), Snapshot(2, false, 3, 1, 2)], FailedDownloadRow = 0 };
        var result = await scenario.Service.QueryAsync(Query());
        Assert.Equal(2, scenario.DownloadCount);
        Assert.Equal(DocumentDownloadStatus.Failed, result.Documents[0].DownloadStatus);
        Assert.Equal(1, result.Documents[0].PageNumber);
        Assert.Equal(1, result.Pagination.DuplicateCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.DownloadedCount);
        Assert.Equal(ExtractionStatus.Partial, result.Status);
    }

    [Fact]
    public async Task Query_UnconfirmedEmptyPageCannotReturnNoDocuments()
    {
        var result = await new Scenario { Pages = [Snapshot(1, false, null)] }.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.Equal(PaginationStatus.Incomplete, result.Pagination.Status);
        Assert.True(result.QuerySucceeded);
    }

    [Fact]
    public async Task Query_NoSavedDocumentsAndIncompleteTraversalIsFailed()
    {
        var result = await new Scenario { Pages = [Snapshot(1, null, null, 1)], FailedDownloadRow = 0 }.Service.QueryAsync(Query());
        Assert.Equal(ExtractionStatus.Failed, result.Status);
        Assert.True(result.QuerySucceeded);
        Assert.Equal(result.DiscoveredCount, result.DownloadedCount + result.FailedCount);
    }

    [Fact]
    public async Task Query_UnreadableRowKeepsItsPageAndPosition()
    {
        var second = Snapshot(2, false, 2) with { Rows = [new(0,
            OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "Bad row"), "unreadable")] };
        var result = await new Scenario { Pages = [Snapshot(1, true, 2, 1), second] }.Service.QueryAsync(Query());
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(PaginationStatus.Completed, result.Pagination.Status);
        Assert.Equal(ExtractionStatus.Partial, result.Status);
        Assert.Equal(2, result.Documents[1].Errors[0].PageNumber);
        Assert.Equal(0, result.Documents[1].Errors[0].RowIndex);
    }


    [Fact]
    public async Task Run_PersistsThousandsIncrementallyWithoutAccumulatingDocuments()
    {
        var scenario = new Scenario { RowCount = 2000 };
        var progress = new StreamingProgress();
        var result = await scenario.Service.RunAsync(Query(), progress, default);
        Assert.Empty(result.Documents);
        Assert.Equal(2000, progress.Count);
        Assert.Equal(2000, result.DownloadedCount);
        Assert.Equal(result.DiscoveredCount, result.DownloadedCount + result.FailedCount);
        Assert.True(scenario.Session.Disposed);
        Assert.All(scenario.Contents, c => Assert.False(c.Stream.CanRead));
    }
    [Fact]
    public async Task Run_PersistenceFailureStopsTraversalAndDisposesSession()
    {
        var scenario = new Scenario { RowCount = 3 };
        var progress = new StreamingProgress { Fail = true };
        await Assert.ThrowsAsync<IOException>(() => scenario.Service.RunAsync(Query(), progress, default));
        Assert.True(progress.QueryConfirmedBeforeSave);
        Assert.Equal(1, scenario.DownloadCount);
        Assert.True(scenario.Session.Disposed);
    }
    private sealed class StreamingProgress : IExtractionProgress
    {
        public bool Fail { get; init; }
        public int Count { get; private set; }
        private bool queryConfirmed;
        public bool QueryConfirmedBeforeSave { get; private set; }
        public Task StageAsync(ExtractionStage stage, CancellationToken token) => Task.CompletedTask;
        public Task<ReceivedDocumentResponse?> FindSavedAsync(string key, int page, int row, CancellationToken token) => Task.FromResult<ReceivedDocumentResponse?>(null);
        public Task SaveAsync(ReceivedDocumentResponse document, CancellationToken token)
        {
            QueryConfirmedBeforeSave = queryConfirmed;
            if (Fail) throw new IOException("Persistence unavailable");
            Count++; return Task.CompletedTask;
        }
        public Task CheckpointAsync(ReceivedDocumentsResponse response, CancellationToken token)
        {
            queryConfirmed = response.QuerySucceeded;
            return Task.CompletedTask;
        }
    }

    internal static ReceivedDocumentsQuery Query(DownloadFormat format = DownloadFormat.Xml) => new()
    {
        CompanyId = "acme", User = "1790012345001", Password = "test-only", Year = 2026, Month = 6, Day = 0,
        DocumentType = DocumentType.Invoice, DownloadFormat = format
    };

    private sealed class Scenario : IReceivedDocumentsSessionFactory, IReceivedDocumentsPage,
        IDocumentDownloader, IDocumentParser, IDocumentStorage, IDocumentValidator
    {
        public IReadOnlyList<ReceivedDocumentsPageSnapshot>? Pages { get; init; }
        public int MaxPages { get; init; } = 1000;
        public bool FailNavigation { get; init; }
        public bool ThrowOnNavigation { get; init; }
        public int Moves { get; private set; }
        public int RowCount { get; init; } = 1;
        public int? FailedReadRow { get; init; }
        public int? FailedDownloadRow { get; init; }
        public string? FailureStage { get; init; }
        public bool ThrowOnSave { get; init; }
        public int? FailedSaveRow { get; init; }
        public bool InvalidAccessKey { get; init; }
        public StorageProvider Provider { get; init; } = StorageProvider.Local;
        public bool InvalidContent { get; init; }
        public bool MetadataIssue { get; init; }
        public bool ThrowOnParse { get; init; }
        public bool ThrowOnOpen { get; init; }
        public IDocumentStorage? StorageOverride { get; init; }
        public IDocumentValidator? ValidatorOverride { get; init; }
        public int DownloadCount { get; private set; }
        public int SaveCount { get; private set; }
        public DocumentStorageContext? LastStorageContext { get; private set; }
        public List<DocumentContent> Contents { get; } = [];
        public FakeSession Session { get; private set; } = null!;
        public ReceivedDocumentsService Service => new(this, this, this, this, StorageOverride ?? this,
            NullLogger<ReceivedDocumentsService>.Instance, Options.Create(new ReceivedDocumentsPaginationOptions { MaxPages = MaxPages }), ValidatorOverride ?? this);
        public IReceivedDocumentsSession Create() => Session = new FakeSession(FailureStage == "login");

        public Task<bool> OpenAsync(IReceivedDocumentsSession session) =>
            ThrowOnOpen ? throw new InvalidOperationException("secret") : Task.FromResult(FailureStage != "portal");
        public Task<OperationResult<ReceivedDocumentsPageSnapshot>> QueryAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query) =>
            Task.FromResult(FailureStage == "query"
                ? OperationResult<ReceivedDocumentsPageSnapshot>.Failure(ExtractionErrorCode.QueryFailed, "Query failed.")
                : OperationResult<ReceivedDocumentsPageSnapshot>.Success(Pages?[0] ?? new(1, false, RowCount,
                    Enumerable.Range(0, RowCount).Select(index => new ReceivedDocumentRowSnapshot(index,
                        FailedReadRow == index
                            ? OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "Row failed.")
                            : OperationResult<ReceivedDocumentReference>.Success(new(
                                new ReceivedDocumentMetadata { AuthorizationNumber = InvalidAccessKey ? "invalid" : index.ToString().PadLeft(49, '0'), MetadataParseStatus = MetadataIssue ? MetadataParseStatus.Partial : MetadataParseStatus.NotChecked, Errors = MetadataIssue ? [new(ExtractionErrorCode.InvalidMetadata, "Invalid amount.", Field: "amount")] : [] },
                                "xml", "pdf", "detail")), index.ToString())).ToArray(), RowCount == 0)));

        public Task<OperationResult<ReceivedDocumentsPageSnapshot>> MoveNextAsync(IReceivedDocumentsSession session, ReceivedDocumentsPageSnapshot current)
        {
            Moves++;
            if (ThrowOnNavigation) throw new IOException("Navigation exception");
            return Task.FromResult(FailNavigation
                ? OperationResult<ReceivedDocumentsPageSnapshot>.Failure(ExtractionErrorCode.PaginationNavigationFailed, "Navigation failed.")
                : OperationResult<ReceivedDocumentsPageSnapshot>.Success(Pages![Moves]));
        }

        public Task<OperationResult<DocumentContent>> DownloadAsync(
            IReceivedDocumentsSession session, ReceivedDocumentReference document, DownloadFormat format, CancellationToken token = default)
        {
            var rowIndex = DownloadCount++;
            if (FailedDownloadRow == rowIndex)
                return Task.FromResult(OperationResult<DocumentContent>.Failure(ExtractionErrorCode.DownloadFailed, "Download failed."));
            var bytes = Encoding.UTF8.GetBytes(format == DownloadFormat.Xml
                ? "<factura id=\"comprobante\" version=\"1.0.0\"><infoTributaria><ruc>1790012345001</ruc></infoTributaria></factura>"
                : "%PDF test");
            var content = new DocumentContent(new MemoryStream(bytes), format);
            Contents.Add(content);
            return Task.FromResult(OperationResult<DocumentContent>.Success(content));
        }

        public string Version => "1";
        public Task<DocumentValidationResult> ValidateAsync(DocumentContent content, DocumentType type, string key, CancellationToken token = default) =>
            Task.FromResult(new DocumentValidationResult { Status = InvalidContent || InvalidAccessKey ? DocumentValidationStatus.Invalid : DocumentValidationStatus.Valid,
                ValidatorVersion = Version, Sha256 = "fixture-hash", SizeBytes = content.Stream.Length,
                Errors = InvalidContent || InvalidAccessKey ? [new(ExtractionErrorCode.InvalidDocument, "Invalid content.")] : [] });

        public OperationResult<string> ExtractXml(string response) => new DocumentParser().ExtractXml(response);
        public Task<DocumentParseResult> ParseAsync(DocumentContent content, DocumentType documentType) =>
            ThrowOnParse ? throw new InvalidOperationException("Parse failure") : new DocumentParser().ParseAsync(content, documentType);

        public Task<DocumentStorageReference> SaveAsync(DocumentStorageContext context, DocumentContent content, CancellationToken cancellationToken = default)
        {
            LastStorageContext = context;
            SaveCount++;
            if (ThrowOnSave || FailedSaveRow == SaveCount - 1) throw new IOException("Disk failure");
            Assert.True(content.Stream.CanRead);
            Assert.Equal(0, content.Stream.Position);
            Assert.Equal("acme", context.CompanyId);
            Assert.Equal("1790012345001", context.TaxpayerId);
            Assert.Equal(DocumentDirection.Received, context.Direction);
            return Task.FromResult(new DocumentStorageReference(Provider, Provider == StorageProvider.Local ? null : "test-bucket",
                DocumentStorageKey.Create(context, content.Format)) { LocalPath = Provider == StorageProvider.Local ? "/test/file" : null });
        }
        public bool CanRead(DocumentStorageReference reference) => true;
        public Task<DocumentStorageInspection> InspectAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentStorageInspection(StorageInspectionStatus.Exists));
        public Task<Stream> OpenReadAsync(DocumentStorageReference reference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSession(bool failLogin) : IReceivedDocumentsSession
    {
        public bool Disposed { get; private set; }
        public IPage Page => throw new NotSupportedException("No browser is required.");
        public Task<SriUserProfile?> LoginAsync(ReceivedDocumentsQuery query) => Task.FromResult<SriUserProfile?>(
            failLogin ? null : new SriUserProfile { TaxpayerId = "", BusinessName = "Test Company" });
        public Task<string> GetPortalBodyAsync() => throw new NotSupportedException();
        public Task SubmitPortalFormAsync(string body) => throw new NotSupportedException();
        public Task CloseModalAsync() => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
