using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Models.Responses;

public class SriUserResponse
{
    [JsonPropertyName("ruc")]
    public string TaxpayerId { get; set; } = "";

    [JsonPropertyName("razonSocial")]
    public string BusinessName { get; set; } = "";

    [JsonPropertyName("totalComprobantes")]
    public int TotalDocuments { get; set; }

    [JsonPropertyName("comprobantes")]
    public List<ReceivedDocumentResponse> Documents { get; set; } = new();

    // TODO: temporary legacy aliases. Remove after SRI services are migrated to English property names.
    [JsonIgnore] public string Ruc { get => TaxpayerId; set => TaxpayerId = value; }
    [JsonIgnore] public string RazonSocial { get => BusinessName; set => BusinessName = value; }
    [JsonIgnore] public int TotalComprobantes { get => TotalDocuments; set => TotalDocuments = value; }
    [JsonIgnore] public List<ReceivedDocumentResponse> Comprobantes { get => Documents; set => Documents = value; }
}
