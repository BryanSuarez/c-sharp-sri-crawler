using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Models.Storage;

public sealed record DocumentStorageReference(StorageProvider Provider, string? Bucket, string Key)
{
    [JsonIgnore] public string? LocalPath { get; init; }
}
