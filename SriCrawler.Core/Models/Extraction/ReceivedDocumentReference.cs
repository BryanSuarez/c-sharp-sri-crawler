namespace DescagaCompronanteSRI.Models.Extraction;

public sealed record ReceivedDocumentMetadata
{
    public string SupplierBusinessName { get; init; } = "";
    public string DocumentTypeName { get; init; } = "";
    public string AuthorizationNumber { get; init; } = "";
    public string IssuedAt { get; init; } = "";
    public string AuthorizedAt { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Taxes { get; init; }
    public decimal Total { get; init; }
    public string RelatedDocuments { get; init; } = "";
}

public sealed record ReceivedDocumentReference(
    ReceivedDocumentMetadata Metadata, string XmlLinkId, string PdfLinkId, string DetailId);
