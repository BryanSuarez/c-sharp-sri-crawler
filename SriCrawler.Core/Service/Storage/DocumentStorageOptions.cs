using DescagaCompronanteSRI.Models.Enums;

namespace DescagaCompronanteSRI.Services.Storage;

public sealed class DocumentStorageOptions
{
    public StorageProvider Provider { get; private set; }
    public string Bucket { get; private set; } = "";
    public string Region { get; private set; } = "";
    public string? ServiceUrl { get; private set; }
    public bool ForcePathStyle { get; private set; }
    public string AccessKeyId { get; private set; } = "";
    public string SecretAccessKey { get; private set; } = "";
    public string? SessionToken { get; private set; }

    public void Load(IConfiguration configuration)
    {
        Provider = configuration["DOCUMENT_STORAGE_PROVIDER"]?.Trim().ToLowerInvariant() switch
        {
            null or "" or "local" => StorageProvider.Local,
            "s3" => StorageProvider.S3,
            "r2" => StorageProvider.R2,
            _ => throw new InvalidOperationException("DOCUMENT_STORAGE_PROVIDER must be Local, S3 or R2.")
        };
        if (Provider == StorageProvider.Local) return;

        Bucket = Required(configuration, "DOCUMENT_STORAGE_BUCKET");
        AccessKeyId = Required(configuration, "AWS_ACCESS_KEY_ID");
        SecretAccessKey = Required(configuration, "AWS_SECRET_ACCESS_KEY");
        SessionToken = configuration["AWS_SESSION_TOKEN"];
        Region = Provider == StorageProvider.R2 ? "auto" : Required(configuration, "AWS_REGION");
        ServiceUrl = configuration["DOCUMENT_STORAGE_SERVICE_URL"]?.Trim();
        if (string.IsNullOrEmpty(ServiceUrl)) ServiceUrl = null;
        if (Provider == StorageProvider.R2 && ServiceUrl is null)
            throw new InvalidOperationException("DOCUMENT_STORAGE_SERVICE_URL is required for R2.");
        if (ServiceUrl is not null && (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/"))
            throw new InvalidOperationException("DOCUMENT_STORAGE_SERVICE_URL must be an HTTPS service endpoint without credentials, path or query.");
        var pathStyle = configuration["DOCUMENT_STORAGE_FORCE_PATH_STYLE"];
        if (!string.IsNullOrWhiteSpace(pathStyle))
        {
            if (!bool.TryParse(pathStyle, out var forcePathStyle))
                throw new InvalidOperationException("DOCUMENT_STORAGE_FORCE_PATH_STYLE must be true or false.");
            ForcePathStyle = forcePathStyle;
        }
    }

    private static string Required(IConfiguration configuration, string name) =>
        !string.IsNullOrWhiteSpace(configuration[name]) ? configuration[name]!.Trim()
            : throw new InvalidOperationException($"{name} is required for remote document storage.");
}
