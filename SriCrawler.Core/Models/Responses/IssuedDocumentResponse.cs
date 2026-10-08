using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Models.Responses;

public class IssuedDocumentResponse
{
    [JsonPropertyName("tipoSerie")]
    public string SeriesType { get; set; } = "";

    [JsonPropertyName("numeroFactura")]
    public string InvoiceNumber { get; set; } = "";

    [JsonPropertyName("claveAcceso")]
    public string AccessKey { get; set; } = "";

    [JsonPropertyName("fechaHoraAuth")]
    public string AuthorizationDateTime { get; set; } = "";

    [JsonPropertyName("fechaEmision")]
    public string IssueDate { get; set; } = "";

    [JsonPropertyName("valorSinImpuestos")]
    public decimal SubtotalWithoutTaxes { get; set; }

    [JsonPropertyName("iva")]
    public decimal Vat { get; set; }

    [JsonPropertyName("importeTotal")]
    public decimal TotalAmount { get; set; }

    [JsonPropertyName("pdfLinkId")]
    public string PdfLinkId { get; set; } = "";

    [JsonPropertyName("docsRelLinkId")]
    public string RelatedDocumentsLinkId { get; set; } = "";

    [JsonPropertyName("idDetalleLinkId")]
    public string DetailLinkId { get; set; } = "";

    [JsonPropertyName("rutaArchivo")]
    public string? FilePath { get; set; }

    // TODO: temporary legacy aliases. Remove after SRI services are migrated to English property names.
    [JsonIgnore] public string TipoSerie { get => SeriesType; set => SeriesType = value; }
    [JsonIgnore] public string NumeroFactura { get => InvoiceNumber; set => InvoiceNumber = value; }
    [JsonIgnore] public string ClaveAcceso { get => AccessKey; set => AccessKey = value; }
    [JsonIgnore] public string FechaHoraAuth { get => AuthorizationDateTime; set => AuthorizationDateTime = value; }
    [JsonIgnore] public string FechaEmision { get => IssueDate; set => IssueDate = value; }
    [JsonIgnore] public decimal ValorSinImpuestos { get => SubtotalWithoutTaxes; set => SubtotalWithoutTaxes = value; }
    [JsonIgnore] public decimal Iva { get => Vat; set => Vat = value; }
    [JsonIgnore] public decimal ImporteTotal { get => TotalAmount; set => TotalAmount = value; }
    [JsonIgnore] public string DocsRelLinkId { get => RelatedDocumentsLinkId; set => RelatedDocumentsLinkId = value; }
    [JsonIgnore] public string IdDetalleLinkId { get => DetailLinkId; set => DetailLinkId = value; }
    [JsonIgnore] public string? RutaArchivo { get => FilePath; set => FilePath = value; }
}
