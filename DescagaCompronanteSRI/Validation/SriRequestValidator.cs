using System.Text.RegularExpressions;
using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Validation;

public static partial class SriRequestValidator
{
    private const int MinSupportedYear = 2000;
    private const int MaxSupportedYear = 2100;

    private static readonly HashSet<int> AllowedDocumentTypeCodes = new()
    {
        (int)DocumentType.Invoice,
        (int)DocumentType.PurchaseSettlement,
        (int)DocumentType.CreditNote,
        (int)DocumentType.DebitNote,
        (int)DocumentType.RemissionGuide,
        (int)DocumentType.Withholding,
        (int)DocumentType.RemissionGuideAlternative
    };

    public static bool TryNormalizeTaxpayerId(string? value, out string taxpayerId, out string error)
    {
        taxpayerId = (value ?? string.Empty)
            .Trim()
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty)
            .Replace(".", string.Empty)
            .Replace(" ", string.Empty);

        if (string.IsNullOrWhiteSpace(taxpayerId))
        {
            error = "El campo usuario es requerido.";
            return false;
        }

        if (!TaxpayerIdRegex().IsMatch(taxpayerId))
        {
            error = "El campo usuario debe ser numérico y tener 10 o 13 dígitos.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidDocumentTypeCode(int documentTypeCode)
    {
        return AllowedDocumentTypeCodes.Contains(documentTypeCode);
    }

    public static DocumentType ToDocumentType(int documentTypeCode)
    {
        return IsValidDocumentTypeCode(documentTypeCode)
            ? (DocumentType)documentTypeCode
            : DocumentType.Invoice;
    }

    public static bool TryValidateReceivedDate(
        string year,
        int month,
        int day,
        out int parsedYear,
        out string error)
    {
        parsedYear = 0;

        if (!int.TryParse(year, out parsedYear) || year.Length != 4)
        {
            error = "El campo anio debe tener 4 dígitos.";
            return false;
        }

        if (!IsSupportedYear(parsedYear))
        {
            error = $"El campo anio debe estar entre {MinSupportedYear} y {MaxSupportedYear}.";
            return false;
        }

        if (!IsValidMonth(month))
        {
            error = "El campo mes debe estar entre 1 y 12.";
            return false;
        }

        if (day < 0 || day > 31)
        {
            error = "El campo dia debe estar entre 0 y 31 para recibidos.";
            return false;
        }

        if (day > 0 && day > DateTime.DaysInMonth(parsedYear, month))
        {
            error = "El campo dia no es válido para el mes/anio indicado.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateIssuedDate(
        int year,
        int month,
        int day,
        out string error)
    {
        if (!IsSupportedYear(year))
        {
            error = $"El campo anio debe estar entre {MinSupportedYear} y {MaxSupportedYear}.";
            return false;
        }

        if (!IsValidMonth(month))
        {
            error = "El campo mes debe estar entre 1 y 12.";
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            error = "El campo dia debe ser válido para el mes/anio indicado.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidAccessKey(string? accessKey)
    {
        return !string.IsNullOrWhiteSpace(accessKey) && AccessKeyRegex().IsMatch(accessKey);
    }

    private static bool IsSupportedYear(int year)
    {
        return year is >= MinSupportedYear and <= MaxSupportedYear;
    }

    private static bool IsValidMonth(int month)
    {
        return month is >= 1 and <= 12;
    }

    [GeneratedRegex(@"^\d{10}(\d{3})?$")]
    private static partial Regex TaxpayerIdRegex();

    [GeneratedRegex(@"^\d{49}$")]
    private static partial Regex AccessKeyRegex();
}
