using System.Security.Cryptography;
using System.Text;

namespace DescagaCompronanteSRI.Models.Extraction;

public sealed record ReceivedDocumentRowSnapshot(
    int RowIndex, OperationResult<ReceivedDocumentReference> Result, string Identity);

public sealed record ReceivedDocumentsPageSnapshot(
    int? PageNumber, bool? HasNextPage, int? ReportedTotalCount,
    IReadOnlyList<ReceivedDocumentRowSnapshot> Rows, bool IsEmptyConfirmed = false,
    ExtractionError? StateError = null)
{
    // Sorting detects a repeated page even if the portal reorders its rows.
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Rows.Select(row => row.Identity).Order(StringComparer.Ordinal)))));
}
