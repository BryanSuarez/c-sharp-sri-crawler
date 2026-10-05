using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;

namespace DescagaCompronanteSRI.Services.ReceivedDocuments;

public sealed class LocalDocumentStorage(IWebHostEnvironment environment) : IDocumentStorage
{
    public async Task<string> SaveAsync(string taxpayerId, string authorizationNumber, DocumentContent content)
    {
        var path = GetPath(taxpayerId, authorizationNumber, content.Format);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        content.Stream.Position = 0;
        await using var destination = File.Create(path);
        await content.Stream.CopyToAsync(destination);
        return path;
    }

    public Task<Stream> OpenReadAsync(string taxpayerId, string authorizationNumber, DownloadFormat format) =>
        Task.FromResult<Stream>(File.OpenRead(GetPath(taxpayerId, authorizationNumber, format)));

    private string GetPath(string taxpayerId, string authorizationNumber, DownloadFormat format)
    {
        var extension = format switch
        {
            DownloadFormat.Xml => ".xml",
            DownloadFormat.Pdf => ".pdf",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        return PathService.CombineSafe(
            Path.Combine(environment.WebRootPath, "recibidos", taxpayerId),
            authorizationNumber, extension, "sin_autorizacion");
    }
}
