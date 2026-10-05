using System.Text.Json.Serialization;
using DescagaCompronanteSRI.Serialization;

namespace DescagaCompronanteSRI.Models.Enums;

[JsonConverter(typeof(ApiEnumConverter<StorageProvider>))]
public enum StorageProvider { Local, S3, R2 }

[JsonConverter(typeof(ApiEnumConverter<DocumentDirection>))]
public enum DocumentDirection { Received, Issued }
