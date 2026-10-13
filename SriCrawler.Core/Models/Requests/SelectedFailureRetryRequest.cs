using System.ComponentModel.DataAnnotations;
using DescagaCompronanteSRI.Jobs;

namespace DescagaCompronanteSRI.Models.Requests;

public sealed class SelectedFailureRetryRequest
{
    [Required] public SelectedRetryMode? Mode { get; init; }
    [Required, MinLength(1), MaxLength(500)] public long[] DocumentIds { get; init; } = [];
    [MaxLength(1024)] public string? Password { get; init; }
    public Guid? ClientRequestId { get; init; }
}
