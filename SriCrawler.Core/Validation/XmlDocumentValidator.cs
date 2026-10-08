using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Validation;

public sealed class XmlDocumentValidator(SriSchemaCatalog schemas, IOptions<DocumentValidationOptions> options) : IDocumentFormatValidator
{
    public string Version => DocumentValidator.CurrentVersion;
    public DownloadFormat Format => DownloadFormat.Xml;
    public async Task<DocumentValidationResult> ValidateAsync(DocumentContent content, DocumentType type, string accessKey, CancellationToken token = default)
    {
        var result = new DocumentValidationResult { ValidatorVersion = Version, ValidatedAt = DateTimeOffset.UtcNow };
        var checks = new List<string>();
        DocumentValidationResult Reject(ExtractionErrorCode code, string message, DocumentValidationStatus status = DocumentValidationStatus.Invalid) =>
            result with { Status = status, Checks = checks.ToArray(), Errors = [new(code, message)] };
        try
        {
            content.Stream.Position = 0;
            result = result with { SizeBytes = content.Stream.Length };
            if (content.Stream.Length == 0 || content.Stream.Length > options.Value.MaxXmlBytes)
                return Reject(ExtractionErrorCode.InputTooLarge, "XML is empty or exceeds the configured size limit.");
            result = result with { Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content.Stream, token)) };
            content.Stream.Position = 0;
            token.ThrowIfCancellationRequested();
            var document = SafeSriXml.Read(content.Stream);
            checks.Add("safeXml");
            var root = document.Root!;
            var expected = type switch { DocumentType.Invoice => "factura", DocumentType.PurchaseSettlement => "liquidacionCompra",
                DocumentType.CreditNote => "notaCredito", DocumentType.DebitNote => "notaDebito",
                DocumentType.Withholding => "comprobanteRetencion", DocumentType.RemissionGuide or DocumentType.RemissionGuideAlternative => "guiaRemision", _ => "" };
            if (root.Name != expected || !SafeSriXml.Codes.TryGetValue(expected, out var code) || root.Element("infoTributaria")?.Element("codDoc")?.Value != code)
                return Reject(ExtractionErrorCode.InvalidDocument, "XML document type does not match the requested type.");
            checks.Add("documentType");
            var key = root.Element("infoTributaria")?.Element("claveAcceso")?.Value;
            if (accessKey.Length != 49 || !accessKey.All(char.IsAsciiDigit) || key != accessKey)
            {
                result = result with { IdentityStatus = DocumentIdentityStatus.Mismatch };
                return Reject(ExtractionErrorCode.DocumentIdentityMismatch, "XML access key does not match the requested document.");
            }
            result = result with { IdentityStatus = DocumentIdentityStatus.Verified };
            checks.Add("accessKey");
            if (content.SourceXml is { } source)
            {
                content.Stream.Position = 0;
                using var reader = new StreamReader(content.Stream, new UTF8Encoding(false, true), false, leaveOpen: true);
                var xml = await reader.ReadToEndAsync(token);
                var outer = SafeSriXml.Read(source);
                if (SafeSriXml.Extract(source).TrimStart('\uFEFF') != xml.TrimStart('\uFEFF'))
                    return Reject(ExtractionErrorCode.InvalidDocument, "Stored XML does not match the downloaded receipt.");
                var numbers = outer.Root!.Name.LocalName is "autorizacion" or "Authorization"
                    ? outer.Root.Elements("numeroAutorizacion").ToArray() : [];
                if (numbers.Length > 1 || numbers.Length == 1 && numbers[0].Value != accessKey)
                    return Reject(ExtractionErrorCode.DocumentIdentityMismatch, "Authorization number does not match the requested document.");
                checks.Add("authorizationEnvelope");
            }
            var schema = schemas.Get(expected, root.Attribute("version")?.Value ?? "", out var schemaName);
            result = result with { Schema = schemaName };
            if (schema is null) return Reject(ExtractionErrorCode.UnsupportedDocumentVersion, "XML schema version is not supported.", DocumentValidationStatus.Unsupported);
            var invalid = false;
            document.Validate(schema, (_, _) => invalid = true);
            token.ThrowIfCancellationRequested();
            if (invalid) return Reject(ExtractionErrorCode.InvalidDocument, "XML does not conform to the official document schema.");
            checks.Add("officialSchema");
            return result with { Status = DocumentValidationStatus.Valid, Checks = checks.ToArray() };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (DecoderFallbackException) { return Reject(ExtractionErrorCode.InvalidDocument, "XML encoding is invalid."); }
        catch (XmlException) { return Reject(ExtractionErrorCode.InvalidDocument, "XML is malformed or contains prohibited declarations."); }
        catch (XmlSchemaException) { return Reject(ExtractionErrorCode.ValidationFailed, "XML schema validation could not be completed.", DocumentValidationStatus.Failed); }
        catch (Exception) { return Reject(ExtractionErrorCode.ValidationFailed, "XML validation could not be completed.", DocumentValidationStatus.Failed); }
        finally { if (content.Stream.CanSeek) content.Stream.Position = 0; }
    }
}
