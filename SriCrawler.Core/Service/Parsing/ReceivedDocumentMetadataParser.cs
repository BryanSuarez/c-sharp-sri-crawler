using System.Globalization;
using System.Text.RegularExpressions;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Validation;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Services.Parsing;

public sealed class ReceivedDocumentMetadataParser(IOptions<DocumentValidationOptions> options)
{
    private readonly TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.PortalTimeZone);
    public ReceivedDocumentMetadata Parse(ReceivedDocumentMetadata metadata, MetadataSourceValues raw)
    {
        var errors = new List<ExtractionError>();
        decimal? Amount(string value, string field)
        {
            var parsed = ParseAmount(value);
            if (parsed is null) errors.Add(new(ExtractionErrorCode.InvalidMetadata, "Amount is missing, ambiguous or invalid.", Field: field));
            return parsed;
        }
        var amount = Amount(raw.Amount, "amount");
        var taxes = Amount(raw.Taxes, "taxes");
        var total = Amount(raw.Total, "total");
        var issued = DateOnly.TryParseExact(raw.IssuedAt.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? (DateOnly?)date : null;
        if (issued is null) errors.Add(new(ExtractionErrorCode.InvalidMetadata, "Issue date is missing or invalid.", Field: "issuedDate"));
        var authorized = ParseAuthorization(raw.AuthorizedAt);
        if (authorized is null) errors.Add(new(ExtractionErrorCode.InvalidMetadata, "Authorization date and time are missing or invalid.", Field: "authorizedAtIso"));
        return metadata with { Amount = amount, Taxes = taxes, Total = total, IssuedAt = raw.IssuedAt, AuthorizedAt = raw.AuthorizedAt,
            IssuedDate = issued, AuthorizedAtIso = authorized, SourceValues = raw,
            AuthorizationTimeZone = authorized is null ? null : HasOffset(raw.AuthorizedAt) ? "explicitOffset" : zone.Id,
            MetadataParseStatus = errors.Count == 0 ? MetadataParseStatus.Parsed : errors.Count == 5 ? MetadataParseStatus.Failed : MetadataParseStatus.Partial,
            Errors = errors };
    }
    public static decimal? ParseAmount(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return null;
        // The portal uses dot decimals. A comma decimal is accepted only when unambiguous.
        string normalized;
        if (Regex.IsMatch(text, @"^[+-]?[0-9]+(?:\.[0-9]+)?$")) normalized = text;
        else if (Regex.IsMatch(text, @"^[+-]?[0-9]{1,3}(?:,[0-9]{3})+\.[0-9]+$")) normalized = text.Replace(",", "");
        else if (Regex.IsMatch(text, @"^[+-]?[0-9]{1,3}(?:\.[0-9]{3})+,[0-9]+$")) normalized = text.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(text, @"^[+-]?[0-9]+,[0-9]+$") && text.Length - text.LastIndexOf(',') - 1 != 3) normalized = text.Replace(',', '.');
        else if (Regex.IsMatch(text, @"^[+-]?[0-9]{1,3}(?:,[0-9]{3}){2,}$")) normalized = text.Replace(",", "");
        else if (Regex.IsMatch(text, @"^[+-]?[0-9]{1,3}(?:\.[0-9]{3}){2,}$")) normalized = text.Replace(".", "");
        else return null;
        static string Canonical(string number)
        {
            var parts = number.TrimStart('+', '-').Split('.');
            var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
            var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
            return (number.StartsWith('-') && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length > 0 ? "." + fraction : "");
        }
        // Remove only insignificant zeros; TryParse must never round significant digits.
        var canonical = Canonical(normalized);
        var digits = canonical.TrimStart('-').Replace(".", "").TrimStart('0');
        if (digits.Length > 29 || canonical.Contains('.') && canonical.Length - canonical.IndexOf('.') - 1 > 28) return null;
        if (!decimal.TryParse(canonical, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed)) return null;
        return canonical == Canonical(parsed.ToString("0.############################", CultureInfo.InvariantCulture)) ? parsed : null;
    }
    private static bool HasOffset(string value) => Regex.IsMatch(value.Trim(), @"(?:Z|[+-][0-9]{2}:[0-9]{2})$");
    private DateTimeOffset? ParseAuthorization(string value)
    {
        value = value.Trim();
        if (HasOffset(value))
        {
            string[] formats = ["dd/MM/yyyy HH:mm:sszzz", "dd/MM/yyyy HH:mm:ss.FFFFFFFzzz", "dd/MM/yyyy HH:mm:ss zzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];
            return DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset) ? offset : null;
        }
        if (!DateTime.TryParseExact(value, ["dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) return null;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local) ? null : new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
