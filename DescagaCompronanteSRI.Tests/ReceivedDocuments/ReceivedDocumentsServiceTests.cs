using System.Text;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
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

    internal static ReceivedDocumentsQuery Query(DownloadFormat format = DownloadFormat.Xml) => new()
    {
        User = "1790012345001", Password = "test-only", Year = 2026, Month = 6, Day = 0,
        DocumentType = DocumentType.Invoice, DownloadFormat = format
    };

    private sealed class Scenario : IReceivedDocumentsSessionFactory, IReceivedDocumentsPage,
        IDocumentDownloader, IDocumentParser, IDocumentStorage
    {
        public int RowCount { get; init; } = 1;
        public int? FailedReadRow { get; init; }
        public int? FailedDownloadRow { get; init; }
        public string? FailureStage { get; init; }
        public bool ThrowOnSave { get; init; }
        public bool ThrowOnParse { get; init; }
        public bool ThrowOnOpen { get; init; }
        public int DownloadCount { get; private set; }
        public int SaveCount { get; private set; }
        public List<DocumentContent> Contents { get; } = [];
        public FakeSession Session { get; private set; } = null!;
        public ReceivedDocumentsService Service => new(this, this, this, this, this,
            NullLogger<ReceivedDocumentsService>.Instance);
        public IReceivedDocumentsSession Create() => Session = new FakeSession(FailureStage == "login");

        public Task<bool> OpenAsync(IReceivedDocumentsSession session) =>
            ThrowOnOpen ? throw new InvalidOperationException("secret") : Task.FromResult(FailureStage != "portal");
        public Task<OperationResult<int>> QueryAsync(IReceivedDocumentsSession session, ReceivedDocumentsQuery query) =>
            Task.FromResult(FailureStage == "query"
                ? OperationResult<int>.Failure(ExtractionErrorCode.QueryFailed, "Query failed.")
                : OperationResult<int>.Success(RowCount));
        public Task<OperationResult<ReceivedDocumentReference>> ReadRowAsync(IReceivedDocumentsSession session, int rowIndex) =>
            Task.FromResult(FailedReadRow == rowIndex
                ? OperationResult<ReceivedDocumentReference>.Failure(ExtractionErrorCode.RowReadFailed, "Row failed.")
                : OperationResult<ReceivedDocumentReference>.Success(new(
                    new ReceivedDocumentMetadata { AuthorizationNumber = rowIndex.ToString().PadLeft(49, '0') },
                    "xml", "pdf", "detail")));

        public Task<OperationResult<DocumentContent>> DownloadAsync(
            IReceivedDocumentsSession session, ReceivedDocumentReference document, DownloadFormat format)
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

        public OperationResult<string> ExtractXml(string response) => new DocumentParser().ExtractXml(response);
        public Task<DocumentParseResult> ParseAsync(DocumentContent content, DocumentType documentType) =>
            ThrowOnParse ? throw new InvalidOperationException("Parse failure") : new DocumentParser().ParseAsync(content, documentType);

        public Task<string> SaveAsync(string taxpayerId, string authorizationNumber, DocumentContent content)
        {
            SaveCount++;
            if (ThrowOnSave) throw new IOException("Disk failure");
            Assert.True(content.Stream.CanRead);
            Assert.Equal(0, content.Stream.Position);
            return Task.FromResult("/test/" + authorizationNumber);
        }
        public Task<Stream> OpenReadAsync(string taxpayerId, string authorizationNumber, DownloadFormat format) =>
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
