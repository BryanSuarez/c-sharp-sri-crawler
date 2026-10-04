using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Models.Requests;

public class ReceivedDocumentsQueryRequest
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

    [Range(0, 31)]
    [JsonPropertyName("dia")]
    public int Day { get; set; }

    [Required]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "El campo anio debe tener 4 dígitos.")]
    [JsonPropertyName("anio")]
    public string Year { get; set; } = "";

    [Range(1, 12)]
    [JsonPropertyName("mes")]
    public int Month { get; set; }

    [Range(1, 7)]
    [JsonPropertyName("comprobante")]
    public int DocumentType { get; set; }

    [Required]
    [JsonPropertyName("descargarXml")]
    public bool DownloadXml { get; set; }
}
