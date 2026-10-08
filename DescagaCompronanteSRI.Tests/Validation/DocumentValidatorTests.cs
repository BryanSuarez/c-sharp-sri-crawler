using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using DescagaCompronanteSRI.Validation;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DescagaCompronanteSRI.Tests.Validation;

public sealed class DocumentValidatorTests
{
    private static readonly SriSchemaCatalog Schemas = new();
    internal static string Fixture(string file = "factura_V1.1.0.xml") => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", file));
    internal static IDocumentValidator Validator(DocumentValidationOptions? options = null)
    {
        var config = Options.Create(options ?? new());
        return new DocumentValidator([new XmlDocumentValidator(Schemas, config), new PdfDocumentValidator(config)]);
    }
    internal static string AccessKey(string xml) => XDocument.Parse(xml).Root!.Element("infoTributaria")!.Element("claveAcceso")!.Value;
    public static IEnumerable<object[]> OfficialXmlFiles => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation"), "*.xml")
        .Select(path => new object[] { Path.GetFileName(path) });
    [Theory]
    [MemberData(nameof(OfficialXmlFiles))]
    public async Task Xml_ValidatesOfficialSchemasForEveryPublishedTypeAndVersion(string file)
    {
        var xml = Fixture(file);
        var type = XDocument.Parse(xml).Root!.Name.LocalName switch { "factura" => DocumentType.Invoice, "notaCredito" => DocumentType.CreditNote,
            "notaDebito" => DocumentType.DebitNote, "liquidacionCompra" => DocumentType.PurchaseSettlement,
            "guiaRemision" => DocumentType.RemissionGuide, "comprobanteRetencion" => DocumentType.Withholding, _ => throw new InvalidOperationException() };
        await using var content = DocumentParserTestsContent(xml);
        var result = await Validator().ValidateAsync(content, type, AccessKey(xml));
        var parsedFixture = XDocument.Parse(xml);
        var schema = Schemas.Get(parsedFixture.Root!.Name.LocalName, parsedFixture.Root.Attribute("version")!.Value, out _)!;
        var diagnostics = new List<string>(); parsedFixture.Validate(schema, (_, e) => diagnostics.Add(e.Message));
        Assert.True(result.Status == DocumentValidationStatus.Valid, $"{file}: {result.Status} / {string.Join(',', diagnostics)} / {string.Join(',', result.Errors.Select(x => x.Message))}");
        Assert.Equal(DocumentIdentityStatus.Verified, result.IdentityStatus);
        Assert.Contains("officialSchema", result.Checks);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))), result.Sha256);
        Assert.Equal(0, content.Stream.Position);
    }
    private static DocumentContent DocumentParserTestsContent(string xml, string? source = null) => new(new MemoryStream(Encoding.UTF8.GetBytes(xml)), DownloadFormat.Xml, source);
    [Fact]
    public async Task Xml_InvalidUtf8IsRejectedWithoutReplacingDocumentCharacters()
    {
        var xml = Fixture();
        var bytes = Encoding.UTF8.GetBytes(xml);
        var position = Encoding.UTF8.GetByteCount(xml[..(xml.IndexOf("<razonSocial>", StringComparison.Ordinal) + "<razonSocial>".Length)]);
        bytes[position] = 0xff;
        await using var content = new DocumentContent(new MemoryStream(bytes), DownloadFormat.Xml);
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator().ValidateAsync(content, DocumentType.Invoice, AccessKey(xml))).Status);
        Assert.Equal(0, content.Stream.Position);
    }
    [Fact]
    public async Task Xml_ValidatesDeclaredEncodingWithoutChangingStoredBytes()
    {
        var xml = Fixture().Replace("UTF-8", "utf-16", StringComparison.OrdinalIgnoreCase);
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();
        await using var content = new DocumentContent(new MemoryStream(bytes), DownloadFormat.Xml);
        var result = await Validator().ValidateAsync(content, DocumentType.Invoice, AccessKey(xml));
        Assert.Equal(DocumentValidationStatus.Valid, result.Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(0, content.Stream.Position);
    }
    [Theory]
    [InlineData("<html>Error</html>")]
    [InlineData("<factura>")]
    [InlineData("<!DOCTYPE factura><factura/>")]
    [InlineData("<!DOCTYPE factura [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><factura>&x;</factura>")]
    public async Task Xml_RejectsUnsafeAndNonDocumentContent(string xml)
    {
        await using var content = DocumentParserTestsContent(xml);
        var result = await Validator().ValidateAsync(content, DocumentType.Invoice, new string('1', 49));
        Assert.Equal(DocumentValidationStatus.Invalid, result.Status);
        Assert.Equal(0, content.Stream.Position);
    }
    [Fact]
    public async Task Xml_DistinguishesUnknownVersionIdentityMismatchAndSchemaFailure()
    {
        var xml = Fixture(); var key = AccessKey(xml);
        await using var unsupported = DocumentParserTestsContent(xml.Replace("version=\"1.1.0\"", "version=\"9.9.9\""));
        Assert.Equal(DocumentValidationStatus.Unsupported, (await Validator().ValidateAsync(unsupported, DocumentType.Invoice, key)).Status);
        await using var mismatch = DocumentParserTestsContent(xml);
        Assert.Equal(DocumentIdentityStatus.Mismatch, (await Validator().ValidateAsync(mismatch, DocumentType.Invoice, new string('1', 49))).IdentityStatus);
        var invalid = XDocument.Parse(xml); invalid.Root!.Element("infoTributaria")!.Element("ruc")!.Remove();
        await using var invalidContent = DocumentParserTestsContent(invalid.ToString());
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator().ValidateAsync(invalidContent, DocumentType.Invoice, key)).Status);
        await using var wrongType = DocumentParserTestsContent(xml);
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator().ValidateAsync(wrongType, DocumentType.CreditNote, key)).Status);
    }
    [Fact]
    public async Task Xml_ValidatesEnvelopeAndRetainsOriginalSignedBytes()
    {
        var xml = Fixture(); var key = AccessKey(xml);
        const string signature = """
            <s:Signature xmlns:s="http://www.w3.org/2000/09/xmldsig#"><s:SignedInfo>
            <s:CanonicalizationMethod Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315"/>
            <s:SignatureMethod Algorithm="http://www.w3.org/2000/09/xmldsig#rsa-sha1"/>
            <s:Reference URI="#comprobante"><s:DigestMethod Algorithm="http://www.w3.org/2000/09/xmldsig#sha1"/><s:DigestValue>AA==</s:DigestValue></s:Reference>
            </s:SignedInfo><s:SignatureValue>AA==</s:SignatureValue></s:Signature>
            """;
        xml = xml.Replace("</factura>", signature + "</factura>");
        var envelope = $"<autorizacion><numeroAutorizacion>{key}</numeroAutorizacion><comprobante><![CDATA[{xml}]]></comprobante></autorizacion>";
        var extracted = new DocumentParser().ExtractXml(envelope);
        Assert.True(extracted.IsSuccess); Assert.Equal(xml, extracted.Value);
        await using var content = DocumentParserTestsContent(xml, envelope);
        Assert.Equal(DocumentValidationStatus.Valid, (await Validator().ValidateAsync(content, DocumentType.Invoice, key)).Status);
        using var reader = new StreamReader(content.Stream, leaveOpen: true);
        Assert.Equal(xml, await reader.ReadToEndAsync());
        await using var badEnvelope = DocumentParserTestsContent(xml, envelope.Replace($"<numeroAutorizacion>{key}", "<numeroAutorizacion>bad"));
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator().ValidateAsync(badEnvelope, DocumentType.Invoice, key)).Status);
        await using var unsafeEnvelope = DocumentParserTestsContent(xml, "<!DOCTYPE autorizacion>" + envelope);
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator().ValidateAsync(unsafeEnvelope, DocumentType.Invoice, key)).Status);
    }
    internal static byte[] Pdf(int pages = 1, string? text = null)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(600, 800);
            if (text is not null) page.AddText(text, 8, new PdfPoint(20, 700), font);
        }
        return builder.Build();
    }
    [Fact]
    public async Task Pdf_ReadsAllPagesAndDoesNotRequireText()
    {
        await using var content = new DocumentContent(new MemoryStream(Pdf(3)), DownloadFormat.Pdf);
        var result = await Validator().ValidateAsync(content, DocumentType.Invoice, new string('1', 49));
        Assert.Equal(DocumentValidationStatus.Valid, result.Status);
        Assert.Equal(DocumentIdentityStatus.NotVerified, result.IdentityStatus);
        Assert.Contains("allPages", result.Checks);
        Assert.Equal(0, content.Stream.Position);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pdf_VerifiesUnambiguousAccessKey(bool matches)
    {
        var key = new string('1', 49);
        await using var content = new DocumentContent(new MemoryStream(Pdf(text: "CLAVE DE ACCESO: " + key)), DownloadFormat.Pdf);
        var result = await Validator().ValidateAsync(content, DocumentType.Invoice, matches ? key : new string('2', 49));
        Assert.Equal(matches ? DocumentValidationStatus.Valid : DocumentValidationStatus.Invalid, result.Status);
        Assert.Equal(matches ? DocumentIdentityStatus.Verified : DocumentIdentityStatus.Mismatch, result.IdentityStatus);
    }
    [Fact]
    public async Task Pdf_RejectsHeaderOnlyTruncationAndExcessivePages()
    {
        foreach (var bytes in new[] { "%PDF-1.4 fake"u8.ToArray(), Pdf()[..^12], Pdf(2) })
        {
            await using var content = new DocumentContent(new MemoryStream(bytes), DownloadFormat.Pdf);
            Assert.Equal(DocumentValidationStatus.Invalid, (await Validator(new() { MaxPdfPages = 1 }).ValidateAsync(content, DocumentType.Invoice, new string('1', 49))).Status);
        }
    }
    [Theory]
    [InlineData("image-only.pdf", DocumentValidationStatus.Valid)]
    [InlineData("encrypted.pdf", DocumentValidationStatus.Invalid)]
    public async Task Pdf_ImagePagesAreValidAndPasswordProtectionIsRejected(string file, DocumentValidationStatus expected)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Validation", file));
        await using var content = new DocumentContent(stream, DownloadFormat.Pdf);
        Assert.Equal(expected, (await Validator().ValidateAsync(content, DocumentType.Invoice, new string('1', 49))).Status);
    }
    [Fact]
    public void ExtractXml_DoesNotChangeSignedPayloadLineEndings()
    {
        var xml = Fixture().Replace("\n", "\r\n");
        var envelope = $"<autorizacion><comprobante><![CDATA[{xml}]]></comprobante></autorizacion>";
        Assert.Equal(xml, new DocumentParser().ExtractXml(envelope).Value);
    }
    [Fact]
    public async Task Pdf_RelatedDocumentKeysDoNotIdentifyTheCurrentRide()
    {
        await using var content = new DocumentContent(new MemoryStream(Pdf(text: "Related document: " + new string('2', 49))), DownloadFormat.Pdf);
        var result = await Validator().ValidateAsync(content, DocumentType.Invoice, new string('1', 49));
        Assert.Equal(DocumentValidationStatus.Valid, result.Status);
        Assert.Equal(DocumentIdentityStatus.NotVerified, result.IdentityStatus);
    }
    [Fact]
    public async Task Validation_EnforcesLimitsAndPropagatesCancellation()
    {
        await using var content = DocumentParserTestsContent(Fixture());
        Assert.Equal(DocumentValidationStatus.Invalid, (await Validator(new() { MaxXmlBytes = 10 }).ValidateAsync(content, DocumentType.Invoice, AccessKey(Fixture()))).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Validator().ValidateAsync(content, DocumentType.Invoice, AccessKey(Fixture()), cancellation.Token));
        Assert.Equal(0, content.Stream.Position);
    }
}
