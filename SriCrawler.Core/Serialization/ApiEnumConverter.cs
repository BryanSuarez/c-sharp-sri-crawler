using System.Text.Json;
using System.Text.Json.Serialization;

namespace DescagaCompronanteSRI.Serialization;

public sealed class ApiEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public ApiEnumConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
}
