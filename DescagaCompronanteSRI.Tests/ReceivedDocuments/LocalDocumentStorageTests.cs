using System.Text;
using DescagaCompronanteSRI.Models.Enums;
using DescagaCompronanteSRI.Models.Extraction;
using DescagaCompronanteSRI.Services.ReceivedDocuments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public class LocalDocumentStorageTests
{
    [Theory]
    [InlineData(DownloadFormat.Xml, ".xml")]
    [InlineData(DownloadFormat.Pdf, ".pdf")]
    public async Task SaveAndRead_PreserveLocationNameAndBytes(DownloadFormat format, string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "sri-storage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalDocumentStorage(new TestEnvironment(root));
            var bytes = Encoding.UTF8.GetBytes(format == DownloadFormat.Xml ? "<factura/>" : "%PDF test");
            await using var content = new DocumentContent(new MemoryStream(bytes), format);
            content.Stream.Position = 3;
            var key = new string('1', 49);
            var path = await storage.SaveAsync("1790012345001", key, content);
            Assert.Equal(Path.Combine(root, "recibidos", "1790012345001", key + extension), path);
            await using var saved = await storage.OpenReadAsync("1790012345001", key, format);
            using var copy = new MemoryStream();
            await saved.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
            Assert.True(content.Stream.CanRead);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
    }
}
