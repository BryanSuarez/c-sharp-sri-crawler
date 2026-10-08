using System.ComponentModel.DataAnnotations;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class ReceivedDocumentsPaginationOptions
{
    [Range(1, int.MaxValue)] public int MaxPages { get; set; } = 1000;
    [Range(1, 3)] public int NavigationAttempts { get; set; } = 3;
    [Range(1, 60000)] public int TransitionTimeoutMilliseconds { get; set; } = 60000;
    [Range(0, 60000)] public int RetryDelayMilliseconds { get; set; } = 2000;
}
