using DescagaCompronanteSRI.Models.Enums;
namespace DescagaCompronanteSRI.Models.Dtos;

public sealed record ReceivedDocumentsQuery
{
    public required string User { get; init; }
    public string? AdditionalUser { get; init; }
    public required string Password { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public int Day { get; init; }
    public DocumentType DocumentType { get; init; }
    public DownloadFormat DownloadFormat { get; init; }
}
