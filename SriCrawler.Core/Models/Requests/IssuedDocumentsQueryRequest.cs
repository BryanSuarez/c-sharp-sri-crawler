using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Models.Requests;

public class IssuedDocumentsQueryRequest
{
    [Required]
    [StringLength(32)]
    [JsonPropertyName("usuario")]
    public string User { get; set; } = "";

    [StringLength(32)]
    [JsonPropertyName("usuarioAdicional")]
    public string? AdditionalUser { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 1)]
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [Range(1, 7)]
    [JsonPropertyName("comprobante")]
    public int DocumentType { get; set; } = 1;

    [Range(2000, 2100)]
    [JsonPropertyName("anio")]
    public int Year { get; set; }

    [Range(1, 12)]
    [JsonPropertyName("mes")]
    public int Month { get; set; }

    [Range(1, 31)]
    [JsonPropertyName("dia")]
    public int Day { get; set; }
}
