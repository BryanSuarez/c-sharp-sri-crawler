using DescagaCompronanteSRI.Services;
using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Models.Responses;

public class ReceivedDocumentResponse
{
    [JsonPropertyName("razonSocial")]
    public string SupplierBusinessName { get; set; } = "";

    [JsonPropertyName("tipoDocumento")]
    public string DocumentTypeName { get; set; } = "";

    [JsonPropertyName("numeroAutorizacion")]
    public string AuthorizationNumber { get; set; } = "";

    [JsonPropertyName("fechaEmision")]
    public string IssuedAt { get; set; } = "";

    [JsonPropertyName("fechaAutorizacion")]
    public string AuthorizedAt { get; set; } = "";

    [JsonPropertyName("importeTotal")]
    public decimal Amount { get; set; }

    [JsonPropertyName("impuestos")]
    public decimal Taxes { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [JsonPropertyName("xmlLinkId")]
    public string XmlLinkId { get; set; } = "";

    [JsonPropertyName("pdfLinkId")]
    public string PdfLinkId { get; set; } = "";

    [JsonPropertyName("documentosRelacionados")]
    public string RelatedDocuments { get; set; } = "";

    [JsonPropertyName("idDetalle")]
    public string DetailId { get; set; } = "";

    [JsonPropertyName("rutaArchivo")]
    public string? FilePath { get; set; }

    [JsonPropertyName("factura")]
    public FacturaXML? Invoice { get; set; }

    [JsonPropertyName("liquidacion")]
    public LiquidacionCompra? PurchaseSettlement { get; set; }

    [JsonPropertyName("notaCredito")]
    public NotaCredito? CreditNote { get; set; }

    [JsonPropertyName("notaDebito")]
    public NotaDebito? DebitNote { get; set; }

    [JsonPropertyName("retencion")]
    public ComprobanteRetencion? Withholding { get; set; }

    [JsonPropertyName("guiaRemision")]
    public GuiaRemision? RemissionGuide { get; set; }

    // TODO: temporary legacy aliases. Remove after SRI services are migrated to English property names.
    [JsonIgnore] public string RazonSocial { get => SupplierBusinessName; set => SupplierBusinessName = value; }
    [JsonIgnore] public string TipoDocumento { get => DocumentTypeName; set => DocumentTypeName = value; }
    [JsonIgnore] public string NumeroAutorizacion { get => AuthorizationNumber; set => AuthorizationNumber = value; }
    [JsonIgnore] public string FechaEmision { get => IssuedAt; set => IssuedAt = value; }
    [JsonIgnore] public string FechaAutorizacion { get => AuthorizedAt; set => AuthorizedAt = value; }
    [JsonIgnore] public decimal ImporteTotal { get => Amount; set => Amount = value; }
    [JsonIgnore] public decimal Impuestos { get => Taxes; set => Taxes = value; }
    [JsonIgnore] public string DocumentosRelacionados { get => RelatedDocuments; set => RelatedDocuments = value; }
    [JsonIgnore] public string IdDetalle { get => DetailId; set => DetailId = value; }
    [JsonIgnore] public string? RutaArchivo { get => FilePath; set => FilePath = value; }
    [JsonIgnore] public FacturaXML? Factura { get => Invoice; set => Invoice = value; }
    [JsonIgnore] public LiquidacionCompra? Liquidacion { get => PurchaseSettlement; set => PurchaseSettlement = value; }
    [JsonIgnore] public NotaCredito? NotaCredito { get => CreditNote; set => CreditNote = value; }
    [JsonIgnore] public NotaDebito? NotaDebito { get => DebitNote; set => DebitNote = value; }
    [JsonIgnore] public ComprobanteRetencion? Retencion { get => Withholding; set => Withholding = value; }
    [JsonIgnore] public GuiaRemision? GuiaRemision { get => RemissionGuide; set => RemissionGuide = value; }
}
