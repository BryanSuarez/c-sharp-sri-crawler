using DescagaCompronanteSRI.Helpers;
using DescagaCompronanteSRI.Models.Dtos;

namespace DescagaCompronanteSRI.Contracts;

public interface ISriLoginService
{
    Task<SriUserProfile?> LoginAsync(
        PlaywrightSession session,
        string user,
        string password,
        string? additionalUser);
}
