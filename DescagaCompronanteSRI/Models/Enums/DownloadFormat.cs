using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Serialization;

namespace DescagaCompronanteSRI.Models.Enums;

[JsonConverter(typeof(ApiEnumConverter<DownloadFormat>))]
public enum DownloadFormat
{
    Xml = 1,
    Pdf = 2
}
