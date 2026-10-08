using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Storage;

public sealed record DocumentStorageContext(
    string CompanyId, string TaxpayerId, int Year, int Month, DocumentDirection Direction,
    DocumentType DocumentType, string AccessKey);
