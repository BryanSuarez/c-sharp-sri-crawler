using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Validation;

namespace DescagaCompronanteSRI.Tests.Validation;

public class SriRequestValidatorTests
{
    [Theory]
    [InlineData("1790012345001", "1790012345001")]
    [InlineData("179 001 2345 001", "1790012345001")]
    public void TryNormalizeTaxpayerId_AcceptsTenOrThirteenDigits(string value, string expected)
    {
        var ok = SriRequestValidator.TryNormalizeTaxpayerId(value, out var taxpayerId, out var error);

        Assert.True(ok, error);
        Assert.Equal(expected, taxpayerId);
    }

    [Fact]
    public void TryNormalizeTaxpayerId_RejectsNonNumericValues()
    {
        var ok = SriRequestValidator.TryNormalizeTaxpayerId("abc", out _, out var error);

        Assert.False(ok);
        Assert.Contains("numérico", error);
    }

    [Fact]
    public void TryValidateReceivedDate_AllowsDayZero()
    {
        var ok = SriRequestValidator.TryValidateReceivedDate("2026", 6, 0, out var parsedYear, out var error);

        Assert.True(ok, error);
        Assert.Equal(2026, parsedYear);
    }

    [Fact]
    public void TryValidateIssuedDate_RejectsInvalidCalendarDay()
    {
        var ok = SriRequestValidator.TryValidateIssuedDate(2026, 2, 30, out var error);

        Assert.False(ok);
        Assert.Contains("dia", error);
    }

    [Fact]
    public void AccessKey_MustHaveFortyNineDigits()
    {
        Assert.True(SriRequestValidator.IsValidAccessKey("1234567890123456789012345678901234567890123456789"));
        Assert.False(SriRequestValidator.IsValidAccessKey("123"));
    }

    [Fact]
    public void ToDocumentType_ReturnsInvoiceWhenCodeIsInvalid()
    {
        Assert.Equal(DocumentType.Invoice, SriRequestValidator.ToDocumentType(999));
    }
}
