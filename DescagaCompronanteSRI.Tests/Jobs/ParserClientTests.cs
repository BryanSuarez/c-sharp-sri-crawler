using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Enums;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Jobs;

public sealed class ParserClientTests
{
    private static readonly string Key = new('1', 49);
    private static HttpResponseMessage Success(string? key = null) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { status = "parsed", parserName = "taxo-sri-xml-2-json", parserVersion = "1.8.0",
            documentType = "factura", documentJson = new { infoTributaria = new { claveAcceso = key ?? Key } } })
    };
    private static (HttpClient Client, HttpSriDocumentJsonParser Parser) Create(Handler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://parser/"), Timeout = Timeout.InfiniteTimeSpan };
        return (client, new(client, Options.Create(new ExtractionJobOptions { ParserTimeoutSeconds = 1 })));
    }
    [Theory]
    [InlineData(400, DocumentParseStatus.Failed, "invalidXml")]
    [InlineData(413, DocumentParseStatus.Failed, "inputTooLarge")]
    [InlineData(422, DocumentParseStatus.Unsupported, "unsupportedDocumentType")]
    public async Task DeterministicFailures_AreNotRetried(int status, DocumentParseStatus expected, string code)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var (client, parser) = Create(handler);
        using (client)
        {
            var result = await parser.ParseAsync("fixture", DocumentType.Invoice, Key, default);
            Assert.Equal(expected, result.Status); Assert.Equal(code, result.ErrorCode); Assert.Equal(1, handler.Calls);
        }
    }
    [Fact]
    public async Task TemporaryServerFailure_IsRetriedOnce()
    {
        var handler = new Handler((n, _) => Task.FromResult(n == 1 ? new(HttpStatusCode.ServiceUnavailable) : Success()));
        var (client, parser) = Create(handler);
        using (client)
        {
            Assert.Equal(DocumentParseStatus.Parsed, (await parser.ParseAsync("fixture", DocumentType.Invoice, Key, default)).Status);
            Assert.Equal(2, handler.Calls);
        }
    }
    [Fact]
    public async Task ConnectionLoss_IsRetriedOnce()
    {
        var handler = new Handler((_, _) => throw new HttpRequestException("fixture connection loss"));
        var (client, parser) = Create(handler);
        using (client)
        {
            Assert.Equal("parserUnavailable", (await parser.ParseAsync("fixture", DocumentType.Invoice, Key, default)).ErrorCode);
            Assert.Equal(2, handler.Calls);
        }
    }
    [Fact]
    public async Task Timeout_IsRetriedOnceButShutdownCancellationIsPropagated()
    {
        var handler = new Handler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Success(); });
        var (client, parser) = Create(handler);
        using (client)
        {
            Assert.Equal("parserUnavailable", (await parser.ParseAsync("fixture", DocumentType.Invoice, Key, default)).ErrorCode);
            Assert.Equal(2, handler.Calls);
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.ParseAsync("fixture", DocumentType.Invoice, Key, stop.Token));
        }
    }
    [Fact]
    public async Task IdentityMismatch_IsRejectedWithoutRetry()
    {
        var handler = new Handler((_, _) => Task.FromResult(Success(new string('2', 49))));
        var (client, parser) = Create(handler);
        using (client)
        {
            Assert.Equal("documentIdentityMismatch", (await parser.ParseAsync("fixture", DocumentType.Invoice, Key, default)).ErrorCode);
            Assert.Equal(1, handler.Calls);
        }
    }
    [Fact]
    public void Credentials_AreBoundToExtractionAndRejectTampering()
    {
        var cipher = new CredentialCipher(Options.Create(new ExtractionJobOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
        var id = Guid.NewGuid();
        var encrypted = cipher.Encrypt("fixture-password", id);
        Assert.Equal("fixture-password", cipher.Decrypt(encrypted, id));
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(encrypted, Guid.NewGuid()));
        var changed = Convert.FromBase64String(encrypted); changed[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(Convert.ToBase64String(changed), id));
        Assert.False(CredentialCipher.IsValidKey("invalid"));
    }
    private sealed class Handler(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(++Calls, token);
    }
}
