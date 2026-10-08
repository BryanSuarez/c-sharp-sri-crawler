namespace DescagaCompronanteSRI.Models.Extraction;

public sealed record ReceivedDocumentMetadata
{
    public string SupplierBusinessName { get; init; } = "";
    public string DocumentTypeName { get; init; } = "";
    public string AuthorizationNumber { get; init; } = "";
    public string IssuedAt { get; init; } = "";
    public string AuthorizedAt { get; init; } = "";
    public decimal? Amount { get; init; }
    public decimal? Taxes { get; init; }
    public decimal? Total { get; init; }
    public DateOnly? IssuedDate { get; init; }
    public DateTimeOffset? AuthorizedAtIso { get; init; }
    public string? AuthorizationTimeZone { get; init; }
    public MetadataSourceValues? SourceValues { get; init; }
    public DescagaCompronanteSRI.Models.Enums.MetadataParseStatus MetadataParseStatus { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExtractionError> Errors { get; init; } = [];
    public string RelatedDocuments { get; init; } = "";
}

public sealed record ReceivedDocumentReference(
    ReceivedDocumentMetadata Metadata, string XmlLinkId, string PdfLinkId, string DetailId);

public sealed record MetadataSourceValues(string Amount, string Taxes, string Total, string IssuedAt, string AuthorizedAt);
