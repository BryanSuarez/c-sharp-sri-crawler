using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Validation;
using DescagaCompronanteSRI.Models.Enums;
namespace DescagaCompronanteSRI.Models.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReceivedDocumentsQueryRequest
{
    [Required, StringLength(64), RegularExpression(StorageIdentityRules.CompanyIdPattern)]
    public string CompanyId { get; set; } = "";
    [Required, StringLength(32)] public string User { get; set; } = "";
    [StringLength(32)] public string? AdditionalUser { get; set; }
    [Required, StringLength(128, MinimumLength = 1)] public string Password { get; set; } = "";
    [Required, Range(2000, 2100)] public int? Year { get; set; }
    [Required, Range(1, 12)] public int? Month { get; set; }
    [Required, Range(0, 31)] public int? Day { get; set; }
    [Required, EnumDataType(typeof(DocumentType))] public DocumentType? DocumentType { get; set; }
    [Required, EnumDataType(typeof(DownloadFormat))] public DownloadFormat? DownloadFormat { get; set; }
}
