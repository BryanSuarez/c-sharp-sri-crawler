using DescagaCompronanteSRI.Helpers;

namespace DescagaCompronanteSRI.Contracts;

public interface IPlaywrightSessionFactory
{
    Task<PlaywrightSession> CreateAsync();
}
