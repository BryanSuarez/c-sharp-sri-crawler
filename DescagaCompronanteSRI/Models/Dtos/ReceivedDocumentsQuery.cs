using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Dtos;

public class ReceivedDocumentsQuery
{
    public string User { get; set; } = "";
    public string? AdditionalUser { get; set; }
    public string Password { get; set; } = "";
    public int Day { get; set; }
    public string Year { get; set; } = "";
    public int Month { get; set; }
    public DocumentType DocumentType { get; set; }
    public DownloadFormat DownloadFormat { get; set; } = DownloadFormat.Xml;

    public bool DownloadXml
    {
        get => DownloadFormat == DownloadFormat.Xml;
        set => DownloadFormat = value ? DownloadFormat.Xml : DownloadFormat.Pdf;
    }

    // TODO: temporary legacy aliases. Remove after SRI services are migrated to English property names.
    [JsonIgnore] public string Usuario { get => User; set => User = value; }
    [JsonIgnore] public string? UsuarioAdicional { get => AdditionalUser; set => AdditionalUser = value; }
    [JsonIgnore] public int Dia { get => Day; set => Day = value; }
    [JsonIgnore] public string Anio { get => Year; set => Year = value; }
    [JsonIgnore] public int Mes { get => Month; set => Month = value; }
    [JsonIgnore] public int Comprobante { get => (int)DocumentType; set => DocumentType = (DocumentType)value; }
    [JsonIgnore] public bool DescargarXml { get => DownloadXml; set => DownloadXml = value; }
}
