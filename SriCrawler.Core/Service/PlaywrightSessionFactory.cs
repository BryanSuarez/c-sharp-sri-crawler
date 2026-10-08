using DescagaCompronanteSRI.Diagnostics;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Services;

public sealed class PlaywrightSessionFactory(ILoggerFactory? loggerFactory = null) : IPlaywrightSessionFactory
{
    public Task<PlaywrightSession> CreateAsync()
    {
        return ExtractionDiagnostics.MeasureAsync(DiagnosticOperation.BrowserStartup, () => PlaywrightSession.CreateAsync(loggerFactory?.CreateLogger<PlaywrightSession>()));
    }
}
