using System.Globalization;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.Parsing;
using DescagaCompronanteSRI.Validation;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Parsing;

public sealed class ReceivedDocumentMetadataParserTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("es-EC")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void Amounts_AreIndependentOfCurrentCulture(string name)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            Assert.Equal(1234.56m, ReceivedDocumentMetadataParser.ParseAmount("1,234.56"));
            Assert.Equal(1234.56m, ReceivedDocumentMetadataParser.ParseAmount("1.234,56"));
            Assert.Equal(100.50m, ReceivedDocumentMetadataParser.ParseAmount("100.50"));
            Assert.Equal(100.50m, ReceivedDocumentMetadataParser.ParseAmount("100,50"));
            Assert.Equal(1234567m, ReceivedDocumentMetadataParser.ParseAmount("1,234,567"));
            Assert.Equal(-1.25m, ReceivedDocumentMetadataParser.ParseAmount("-1.25"));
            Assert.Equal(0m, ReceivedDocumentMetadataParser.ParseAmount("0"));
            Assert.Equal(1.234m, ReceivedDocumentMetadataParser.ParseAmount("1.234"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData("")]
    [InlineData("N/A")]
    [InlineData("1,234")]
    [InlineData("12,34.56")]
    [InlineData("1e2")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.12345678901234567890123456789")]
    public void InvalidAmounts_AreNullInsteadOfZero(string value) => Assert.Null(ReceivedDocumentMetadataParser.ParseAmount(value));

    [Fact]
    public void ExactBoundaryValues_DoNotRoundOrRejectInsignificantZeros()
    {
        Assert.Equal(decimal.MaxValue, ReceivedDocumentMetadataParser.ParseAmount("79228162514264337593543950335.0"));
        Assert.Equal(0m, ReceivedDocumentMetadataParser.ParseAmount("-0.000000000000000000000000000000000"));
        Assert.Equal(0.0000000000000000000000000001m, ReceivedDocumentMetadataParser.ParseAmount("0.000000000000000000000000000100"));
        Assert.Null(ReceivedDocumentMetadataParser.ParseAmount("7922816251426433759354395033.51"));
    }

    [Fact]
    public void Metadata_RecordsSourceAndDatesWithoutUsingHostTimeZone()
    {
        var parser = new ReceivedDocumentMetadataParser(Options.Create(new DocumentValidationOptions()));
        var values = new MetadataSourceValues("1.00", "0", "1.00", "29/02/2024", "29/02/2024 10:30:00.123");
        var result = parser.Parse(new(), values);
        Assert.Equal(new DateOnly(2024, 2, 29), result.IssuedDate);
        Assert.Equal(TimeSpan.FromHours(-5), result.AuthorizedAtIso!.Value.Offset);
        Assert.Equal(123, result.AuthorizedAtIso.Value.Millisecond);
        Assert.Equal("America/Guayaquil", result.AuthorizationTimeZone);
        Assert.Equal(values, result.SourceValues);
        Assert.Equal(MetadataParseStatus.Parsed, result.MetadataParseStatus);
        Assert.Empty(result.Errors);
    }
    [Fact]
    public void InvalidDatesAndAmounts_AreExplicitWithoutDiscardingOtherFields()
    {
        var parser = new ReceivedDocumentMetadataParser(Options.Create(new DocumentValidationOptions()));
        var result = parser.Parse(new(), new("invalid", "0", "2.50", "31/02/2026", "01/09/2026"));
        Assert.Null(result.Amount); Assert.Null(result.IssuedDate); Assert.Null(result.AuthorizedAtIso);
        Assert.Equal(0m, result.Taxes); Assert.Equal(2.50m, result.Total);
        Assert.Equal(MetadataParseStatus.Partial, result.MetadataParseStatus);
        Assert.Equal(new[] { "amount", "issuedDate", "authorizedAtIso" }, result.Errors.Select(x => x.Field));
    }
    [Fact]
    public void ExplicitOffset_IsPreserved()
    {
        var parser = new ReceivedDocumentMetadataParser(Options.Create(new DocumentValidationOptions()));
        var result = parser.Parse(new(), new("1", "0", "1", "01/09/2026", "2026-09-01T12:00:00+02:00"));
        Assert.Equal(TimeSpan.FromHours(2), result.AuthorizedAtIso!.Value.Offset);
        Assert.Equal("explicitOffset", result.AuthorizationTimeZone);
    }
    [Theory]
    [InlineData("10/03/2024 02:30:00")]
    [InlineData("03/11/2024 01:30:00")]
    public void AmbiguousOrNonexistentLocalTime_IsRejected(string value)
    {
        var parser = new ReceivedDocumentMetadataParser(Options.Create(new DocumentValidationOptions { PortalTimeZone = "America/New_York" }));
        Assert.Null(parser.Parse(new(), new("1", "0", "1", "01/09/2026", value)).AuthorizedAtIso);
    }
}
