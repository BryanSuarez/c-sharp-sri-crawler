using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DescagaCompronanteSRI.ApiSecurity;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Security;
using DescagaCompronanteSRI.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DescagaCompronanteSRI.Controllers;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateCompanyApiKeyRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
}

[ApiController, Authorize(Policy = ApiAccess.AdminPolicy)]
[Route("api/admin/companies/{companyId}/api-keys")]
public sealed class CompanyApiKeysController(ICompanyApiKeys keys, ILogger<CompanyApiKeysController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CreatedCompanyApiKey), 201)]
    public async Task<IActionResult> Create([RegularExpression(StorageIdentityRules.CompanyIdPattern), StringLength(64)] string companyId,
        [FromBody] CreateCompanyApiKeyRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { code = "invalidName", message = "Name is required." });
        try
        {
            var created = await keys.CreateAsync(companyId, request.Name, request.ExpiresAt, AdministratorId(), token);
            logger.LogInformation(new EventId(1402, "CompanyApiKeyCreated"), "API key created: {keyId}, company {companyId}, administrator {administratorId}.", created.Key.KeyId, companyId, AdministratorId());
            // Do not use CreatedAtAction: the existing transport does not need a Location header.
            return StatusCode(201, created);
        }
        catch (ArgumentException) { return BadRequest(new { code = "invalidExpiration", message = "Expiration must be in the future." }); }
        catch (ApiKeyPersistenceUnavailableException) { return Unavailable(); }
    }
    [HttpGet]
    [ProducesResponseType(typeof(CursorPage<CompanyApiKeySummary>), 200)]
    public async Task<IActionResult> List([RegularExpression(StorageIdentityRules.CompanyIdPattern), StringLength(64)] string companyId,
        CancellationToken token, [FromQuery] long cursor = 0, [FromQuery] int limit = 100)
    {
        if (cursor < 0 || limit is < 1 or > 500) return BadRequest(new { message = "Invalid cursor or limit." });
        try { return Ok(await keys.ListAsync(companyId, cursor, limit, token)); }
        catch (ApiKeyPersistenceUnavailableException) { return Unavailable(); }
    }
    [HttpDelete("{keyId:guid}")]
    public async Task<IActionResult> Revoke([RegularExpression(StorageIdentityRules.CompanyIdPattern), StringLength(64)] string companyId,
        Guid keyId, CancellationToken token)
    {
        try
        {
            if (!await keys.RevokeAsync(companyId, keyId, AdministratorId(), token)) return NotFound();
            logger.LogInformation(new EventId(1403, "CompanyApiKeyRevoked"), "API key revoked: {keyId}, company {companyId}, administrator {administratorId}.", keyId, companyId, AdministratorId());
            return NoContent();
        }
        catch (ApiKeyPersistenceUnavailableException) { return Unavailable(); }
    }
    private Guid AdministratorId() => Guid.ParseExact(User.FindFirstValue(ClaimTypes.NameIdentifier)!, "N");
    private ObjectResult Unavailable() => StatusCode(503, new { code = "persistenceUnavailable", message = "Credential persistence is temporarily unavailable." });
}
