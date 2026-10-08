using DescagaCompronanteSRI.Services;

namespace DescagaCompronanteSRI.Tests.ReceivedDocuments;

public sealed class SriLoginServiceTests
{
    [Theory]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil#session_state=fixture&code=fixture", true)]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea//contribuyente/perfil#code=fixture", true)]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil", false)]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil#code=", false)]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil#code=fixture&error=access_denied", false)]
    [InlineData("https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil?code=fixture", false)]
    [InlineData("https://example.com/sri-en-linea/contribuyente/perfil#code=fixture", false)]
    [InlineData("http://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil#code=fixture", false)]
    [InlineData("https://srienlinea.sri.gob.ec:444/sri-en-linea/contribuyente/perfil#code=fixture", false)]
    [InlineData("https://user@srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil#code=fixture", false)]
    public void AuthorizationRedirect_RequiresTrustedOriginPathAndNonErrorCode(string url, bool expected)
        => Assert.Equal(expected, SriLoginService.IsAuthorizationRedirect(url));
}
