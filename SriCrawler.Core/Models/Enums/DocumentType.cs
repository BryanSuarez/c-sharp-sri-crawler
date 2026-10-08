using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Serialization;

namespace DescagaCompronanteSRI.Models.Enums;

[JsonConverter(typeof(ApiEnumConverter<DocumentType>))]
public enum DocumentType
{
    Invoice = 1,
    PurchaseSettlement = 2,
    CreditNote = 3,
    DebitNote = 4,
    RemissionGuide = 5,
    Withholding = 6,
    RemissionGuideAlternative = 7
}
