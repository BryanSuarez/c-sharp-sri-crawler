using System.Globalization;
using System.Text.RegularExpressions;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Storage;

namespace DescagaCompronanteSRI.Services.Storage;

public static partial class DocumentStorageKey
{
    public static string Create(DocumentStorageContext context, DownloadFormat format)
    {
        if (context.Year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(context.Year), "Storage year must be between 2000 and 2100.");
        if (context.Month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(context.Month), "Storage month must be between 1 and 12.");
        var year = context.Year.ToString("D4", CultureInfo.InvariantCulture);
        var month = context.Month.ToString("D2", CultureInfo.InvariantCulture);
        var direction = context.Direction switch
        {
            DocumentDirection.Received => "received",
            DocumentDirection.Issued => "issued",
            _ => throw new ArgumentOutOfRangeException(nameof(context.Direction))
        };
        var documentType = context.DocumentType switch
        {
            DocumentType.Invoice => "invoice",
            DocumentType.PurchaseSettlement => "purchaseSettlement",
            DocumentType.CreditNote => "creditNote",
            DocumentType.DebitNote => "debitNote",
            DocumentType.Withholding => "withholding",
            DocumentType.RemissionGuide or DocumentType.RemissionGuideAlternative => "remissionGuide",
            _ => throw new ArgumentOutOfRangeException(nameof(context.DocumentType))
        };
        var extension = format switch
        {
            DownloadFormat.Xml => "xml",
            DownloadFormat.Pdf => "pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var key = $"{context.CompanyId}/{context.TaxpayerId}/{year}/{month}/{direction}/{documentType}/{context.AccessKey}.{extension}";
        Validate(key);
        return key;
    }

    public static void Validate(string key)
    {
        if (string.IsNullOrEmpty(key) || !KeyRegex().IsMatch(key))
            throw new ArgumentException("Document storage identity is invalid. Expected a company, taxpayer, year, two-digit month, direction, document type and 49-digit access key.");
    }

    public static string ContentType(DownloadFormat format) => format switch
    {
        DownloadFormat.Xml => "application/xml",
        DownloadFormat.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?/[0-9]{10}(?:[0-9]{3})?/(?:20[0-9]{2}|2100)/(?:0[1-9]|1[0-2])/(?:received|issued)/(?:invoice|purchaseSettlement|creditNote|debitNote|withholding|remissionGuide)/[0-9]{49}\.(?:xml|pdf)\z")]
    private static partial Regex KeyRegex();
}
