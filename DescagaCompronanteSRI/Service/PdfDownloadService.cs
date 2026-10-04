using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Services;

public sealed class PdfDownloadService : IPdfDownloadService
{
    public async Task<bool> DownloadAsync(
        PlaywrightSession session,
        string pdfLinkId,
        string destinationPath,
        string logPrefix = "[PDF]")
    {
        if (string.IsNullOrEmpty(pdfLinkId))
        {
            Console.WriteLine($"{logPrefix} ✗ PdfLinkId vacío.");
            return false;
        }

        Console.WriteLine($"{logPrefix} LinkId={pdfLinkId}");

        for (var attempt = 1; attempt <= SriRetryPolicy.DownloadAttempts; attempt++)
        {
            try
            {
                Console.WriteLine($"{logPrefix} Intento {attempt}/{SriRetryPolicy.DownloadAttempts}...");
                var download = await session.Page.RunAndWaitForDownloadAsync(async () =>
                    await session.Page.EvaluateAsync(@"lid => {
                        const f = document.getElementById('frmPrincipal');
                        if      (typeof mojarra    !== 'undefined') mojarra.jsfcljs(f, {[lid]:lid}, '');
                        else if (typeof PrimeFaces !== 'undefined') PrimeFaces.ab({ s:lid, u:lid });
                        else document.getElementById(lid)?.click();
                    }", pdfLinkId),
                    new() { Timeout = 40_000 });

                await download.SaveAsAsync(destinationPath);

                if (!await IsPdfAsync(destinationPath))
                {
                    var header = await ReadHeaderAsync(destinationPath);
                    Console.WriteLine($"{logPrefix} ✗ No es PDF válido (bytes: {header}).");
                    TryDelete(destinationPath);
                    await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
                    continue;
                }

                Console.WriteLine($"{logPrefix} ✓ Guardado: {Path.GetFileName(destinationPath)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{logPrefix} ✗ Excepción intento {attempt}: {ex.Message}");
                await Task.Delay(SriRetryPolicy.DownloadBackoff(attempt));
            }
        }

        Console.WriteLine($"{logPrefix} ✗ Falló {SriRetryPolicy.DownloadAttempts} intentos.");
        return false;
    }

    private static async Task<bool> IsPdfAsync(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var header = new byte[4];
        await using var fs = File.OpenRead(path);
        var read = await fs.ReadAsync(header.AsMemory(0, 4));

        return read == 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;
    }

    private static async Task<string> ReadHeaderAsync(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        var header = new byte[4];
        await using var fs = File.OpenRead(path);
        var read = await fs.ReadAsync(header.AsMemory(0, 4));
        return string.Concat(header.Take(read).Select(b => b.ToString("X2")));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
