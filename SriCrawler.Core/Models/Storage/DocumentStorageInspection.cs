namespace DescagaCompronanteSRI.Models.Storage;

public enum StorageInspectionStatus { Exists, Missing, Failed }

// Revision is an opaque change token, never a content checksum.
public sealed record DocumentStorageInspection(StorageInspectionStatus Status, long? SizeBytes = null, string? Revision = null);
