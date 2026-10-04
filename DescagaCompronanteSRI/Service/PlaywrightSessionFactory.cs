using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Services;

public sealed class PlaywrightSessionFactory : IPlaywrightSessionFactory
{
    public Task<PlaywrightSession> CreateAsync()
    {
        return PlaywrightSession.CreateAsync();
    }
}
