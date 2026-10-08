using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace DescagaCompronanteSRI.Validation;

public sealed class PdfDocumentValidator(IOptions<DocumentValidationOptions> options) : IDocumentFormatValidator
{
    public string Version => DocumentValidator.CurrentVersion;
    public DownloadFormat Format => DownloadFormat.Pdf;
    public async Task<DocumentValidationResult> ValidateAsync(DocumentContent content, DocumentType type, string accessKey, CancellationToken token = default)
    {
        var result = new DocumentValidationResult { ValidatorVersion = Version, ValidatedAt = DateTimeOffset.UtcNow };
        var checks = new List<string>();
        DocumentValidationResult Reject(string message, ExtractionErrorCode code = ExtractionErrorCode.InvalidDocument,
            DocumentValidationStatus status = DocumentValidationStatus.Invalid) =>
            result with { Status = status, Checks = checks.ToArray(), Errors = [new(code, message)] };
        try
        {
            content.Stream.Position = 0;
            result = result with { SizeBytes = content.Stream.Length };
            if (content.Stream.Length == 0 || content.Stream.Length > options.Value.MaxPdfBytes)
                return Reject("PDF is empty or exceeds the configured size limit.", ExtractionErrorCode.InputTooLarge);
            result = result with { Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content.Stream, token)) };
            content.Stream.Position = 0;
            byte[] header = new byte[8];
            if (await content.Stream.ReadAsync(header, token) != header.Length || !Regex.IsMatch(Encoding.ASCII.GetString(header), @"^%PDF-[12]\.[0-9]"))
                return Reject("PDF header is invalid.");
            content.Stream.Position = Math.Max(0, content.Stream.Length - 2048);
            using (var tail = new StreamReader(content.Stream, Encoding.ASCII, false, leaveOpen: true))
            {
                var end = await tail.ReadToEndAsync(token);
                if (!Regex.IsMatch(end, @"startxref\s+[0-9]+\s+%%EOF[\x00\s]*$")) return Reject("PDF closing structure is missing or truncated.");
            }
            checks.Add("headerAndTrailer");
            content.Stream.Position = 0;
            // PdfPig owns the supplied stream; use a bounded copy to preserve the download stream.
            using var copy = new MemoryStream();
            await content.Stream.CopyToAsync(copy, token);
            using var pdf = PdfDocument.Open(copy.ToArray(), new ParsingOptions { UseLenientParsing = false });
            if (pdf.NumberOfPages < 1) return Reject("PDF has no pages.");
            if (pdf.NumberOfPages > options.Value.MaxPdfPages)
                return Reject("PDF exceeds the configured page limit.", ExtractionErrorCode.InputTooLarge);
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            for (var number = 1; number <= pdf.NumberOfPages; number++)
            {
                token.ThrowIfCancellationRequested();
                var page = pdf.GetPage(number);
                foreach (Match match in Regex.Matches(page.Text, @"(?:CLAVE\s*DE\s*ACCESO|N[ÚU]MERO\s*DE\s*AUTORIZACI[ÓO]N)\s*:?\s*([0-9]{49})(?![0-9])", RegexOptions.IgnoreCase)) candidates.Add(match.Groups[1].Value);
            }
            checks.Add("allPages");
            if (candidates.Count == 1)
            {
                result = result with { IdentityStatus = candidates.Single() == accessKey ? DocumentIdentityStatus.Verified : DocumentIdentityStatus.Mismatch };
                if (result.IdentityStatus == DocumentIdentityStatus.Mismatch) return Reject("PDF access key does not match the requested document.", ExtractionErrorCode.DocumentIdentityMismatch);
                checks.Add("accessKey");
            }
            return result with { Status = DocumentValidationStatus.Valid, Checks = checks.ToArray() };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException || e.GetType().Namespace?.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal) == true)
        { return Reject("PDF is damaged, encrypted or cannot be read structurally."); }
        catch (Exception) { return Reject("PDF validation could not be completed.", ExtractionErrorCode.ValidationFailed, DocumentValidationStatus.Failed); }
        finally { if (content.Stream.CanSeek) content.Stream.Position = 0; }
    }
}
