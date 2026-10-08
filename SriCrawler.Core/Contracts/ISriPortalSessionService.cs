using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Contracts;

public interface ISriPortalSessionService
{
    Task<string> GetPortalBodyAsync(
        PlaywrightSession session,
        string primaryUrl,
        string fallbackUrl,
        string logPrefix = "[Portal]");

    Task SubmitJSecurityCheckAsync(PlaywrightSession session, string body);
}
