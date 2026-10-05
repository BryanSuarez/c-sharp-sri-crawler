using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Services.Storage;

namespace DescagaCompronanteSRI.Tests.Storage;

public class DocumentStorageKeyTests
{
    [Theory]
    [InlineData(DocumentType.Invoice, "invoice")]
    [InlineData(DocumentType.PurchaseSettlement, "purchaseSettlement")]
    [InlineData(DocumentType.CreditNote, "creditNote")]
    [InlineData(DocumentType.DebitNote, "debitNote")]
    [InlineData(DocumentType.Withholding, "withholding")]
    [InlineData(DocumentType.RemissionGuide, "remissionGuide")]
    [InlineData(DocumentType.RemissionGuideAlternative, "remissionGuide")]
    public void Create_UsesStableEnglishTypeNames(DocumentType type, string folder)
    {
        var context = StorageTestSupport.Context with { DocumentType = type };
        Assert.Equal($"acme/1790012345001/2026/06/received/{folder}/{context.AccessKey}.xml",
            DocumentStorageKey.Create(context, DownloadFormat.Xml));
        Assert.Equal($"acme/1790012345001/2026/06/issued/{folder}/{context.AccessKey}.pdf",
            DocumentStorageKey.Create(context with { Direction = DocumentDirection.Issued }, DownloadFormat.Pdf));
    }

    [Fact]
    public void Create_SeparatesCompaniesTaxpayersAndFormats()
    {
        var context = StorageTestSupport.Context;
        var original = DocumentStorageKey.Create(context, DownloadFormat.Xml);
        Assert.NotEqual(original, DocumentStorageKey.Create(context with { CompanyId = "other" }, DownloadFormat.Xml));
        Assert.NotEqual(original, DocumentStorageKey.Create(context with { TaxpayerId = "1719956854" }, DownloadFormat.Xml));
        Assert.Equal(Path.ChangeExtension(original, "pdf"), DocumentStorageKey.Create(context, DownloadFormat.Pdf));
        Assert.Equal(original, DocumentStorageKey.Create(context, DownloadFormat.Xml));
        Assert.StartsWith("a/", DocumentStorageKey.Create(context with { CompanyId = "a" }, DownloadFormat.Xml));
        Assert.StartsWith(new string('a', 64) + "/", DocumentStorageKey.Create(context with { CompanyId = new string('a', 64) }, DownloadFormat.Xml));
    }

    [Theory]
    [InlineData(2000, 1, "2000/01")]
    [InlineData(2026, 9, "2026/09")]
    [InlineData(2100, 12, "2100/12")]
    public void Create_PadsMonthAndSeparatesRequestedPeriods(int year, int month, string period)
    {
        var context = StorageTestSupport.Context with { Year = year, Month = month };
        var key = DocumentStorageKey.Create(context, DownloadFormat.Xml);
        Assert.StartsWith($"acme/1790012345001/{period}/received/invoice/", key);
        Assert.NotEqual(key, DocumentStorageKey.Create(context with { Year = year == 2000 ? 2001 : year - 1 }, DownloadFormat.Xml));
        Assert.NotEqual(key, DocumentStorageKey.Create(context with { Month = month == 12 ? 1 : month + 1 }, DownloadFormat.Xml));
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void Create_RejectsInvalidPeriods(int year, int month) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        DocumentStorageKey.Create(StorageTestSupport.Context with { Year = year, Month = month }, DownloadFormat.Xml));

    [Theory]
    [InlineData("2026/1")]
    [InlineData("2026/00")]
    [InlineData("2026/13")]
    [InlineData("1999/01")]
    [InlineData("2101/01")]
    [InlineData("2026/../01")]
    public void Validate_RejectsMalformedPeriodInReadReference(string period) => Assert.Throws<ArgumentException>(() =>
        DocumentStorageKey.Validate($"acme/1790012345001/{period}/received/invoice/{new string('1', 49)}.xml"));

    [Theory]
    [InlineData("../acme")]
    [InlineData("acme/other")]
    [InlineData("acme\\other")]
    [InlineData("ACME")]
    [InlineData("acme ")]
    [InlineData("acme\n")]
    [InlineData("-acme")]
    [InlineData("")]
    public void Create_RejectsUnsafeCompany(string company) => Assert.Throws<ArgumentException>(() =>
        DocumentStorageKey.Create(StorageTestSupport.Context with { CompanyId = company }, DownloadFormat.Xml));

    [Fact]
    public void Create_RejectsInvalidIdentifiersAndEnums()
    {
        var context = StorageTestSupport.Context;
        Assert.Throws<ArgumentException>(() => DocumentStorageKey.Create(context with { CompanyId = new string('a', 65) }, DownloadFormat.Xml));
        foreach (var key in new[] { "", "../document", new string('1', 48), new string('1', 50), new string('١', 49), context.AccessKey + "\n" })
            Assert.Throws<ArgumentException>(() => DocumentStorageKey.Create(context with { AccessKey = key }, DownloadFormat.Xml));
        Assert.Throws<ArgumentException>(() => DocumentStorageKey.Create(context with { TaxpayerId = "../1790012345001" }, DownloadFormat.Xml));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentStorageKey.Create(context, (DownloadFormat)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentStorageKey.Create(context with { DocumentType = (DocumentType)99 }, DownloadFormat.Xml));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentStorageKey.Create(context with { Direction = (DocumentDirection)99 }, DownloadFormat.Xml));
    }
}
