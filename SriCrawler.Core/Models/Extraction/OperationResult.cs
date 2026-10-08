using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Extraction;

public sealed record ExtractionError(ExtractionErrorCode Code, string Message, int? RowIndex = null, int? PageNumber = null);

public sealed record OperationResult<T>(T? Value, ExtractionError? Error)
{
    public bool IsSuccess => Error is null;
    public static OperationResult<T> Success(T value) => new(value, null);
    public static OperationResult<T> Failure(ExtractionErrorCode code, string message) =>
        new(default, new ExtractionError(code, message));
}
