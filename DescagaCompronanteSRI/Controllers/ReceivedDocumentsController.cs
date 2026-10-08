using System.Globalization;
using System.Text.Json;
using DescagaCompronanteSRI.Jobs;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Requests;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DescagaCompronanteSRI.Controllers;

[ApiController]
[Route("api/received-documents")]
public sealed class ReceivedDocumentsController(IExtractionJobs jobs, IOptions<ExtractionJobOptions> options) : ControllerBase
{
    /// <summary>Accepts a persistent extraction. Async is the default; sync waits for at most the configured interval.</summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(ExtractionAccepted), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ReceivedDocumentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReceivedDocumentsResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Query([FromBody] ReceivedDocumentsQueryRequest request, CancellationToken token)
    {
        if (!SriRequestValidator.TryNormalizeTaxpayerId(request.User, out var taxpayerId, out _))
            ModelState.AddModelError("user", "User must contain 10 or 13 digits.");
        if (!SriRequestValidator.TryValidateReceivedDate(request.Year!.Value.ToString(CultureInfo.InvariantCulture),
            request.Month!.Value, request.Day!.Value, out _, out _))
            ModelState.AddModelError("date", "The requested date is invalid. Day 0 selects the entire month.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var query = new ReceivedDocumentsQuery
        {
            CompanyId = request.CompanyId, User = taxpayerId, AdditionalUser = string.IsNullOrWhiteSpace(request.AdditionalUser) ? null : request.AdditionalUser.Trim(),
            Password = request.Password, Year = request.Year.Value, Month = request.Month.Value, Day = request.Day.Value,
            DocumentType = request.DocumentType!.Value, DownloadFormat = request.DownloadFormat!.Value, DownloadPolicy = request.DownloadPolicy
        };
        try
        {
            var accepted = await jobs.AcceptAsync(query, request.ClientRequestId, token);
            if (request.ExecutionMode == ExecutionMode.Sync)
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Value.SyncWaitSeconds);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    var result = await jobs.ResultAsync(accepted.ExtractionId, query.CompanyId, token);
                    if (result is not null) return result.QuerySucceeded ? Ok(result) : StatusCode(500, result);
                    await Task.Delay(500, token);
                }
            }
            // Return URLs in the body; no additional HTTP headers are introduced.
            return StatusCode(StatusCodes.Status202Accepted, accepted);
        }
        catch (IdempotencyConflictException) { return Conflict(new { code = "idempotencyConflict", message = "clientRequestId was already used with different parameters." }); }
        catch (Exception e) when (e is PersistenceUnavailableException or NpgsqlException)
        { return StatusCode(503, new { code = "persistenceUnavailable", message = "Extraction persistence is temporarily unavailable." }); }
    }
    [HttpGet("extractions/{id:guid}")]
    [ProducesResponseType(typeof(ExtractionSummary), 200)]
    public async Task<IActionResult> Status(Guid id, [FromQuery] string companyId, CancellationToken token)
    {
        var result = await jobs.GetAsync(id, companyId, token);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("extractions/{id:guid}/documents")]
    [ProducesResponseType(typeof(CursorPage<JsonElement>), 200)]
    public async Task<IActionResult> Documents(Guid id, [FromQuery] string companyId, CancellationToken token,
        [FromQuery] long cursor = 0, [FromQuery] int limit = 100)
    {
        if (cursor < 0 || limit is < 1 or > 500) return BadRequest(new { message = "Invalid cursor or limit." });
        var result = await jobs.DocumentsAsync(id, companyId, cursor, limit, token);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("extractions/{id:guid}/documents/{documentId:long}")]
    public async Task<IActionResult> Document(Guid id, long documentId, [FromQuery] string companyId, CancellationToken token)
    {
        var result = await jobs.DocumentAsync(id, companyId, documentId, token);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("extractions/{id:guid}/errors")]
    [ProducesResponseType(typeof(CursorPage<JsonElement>), 200)]
    public async Task<IActionResult> Errors(Guid id, [FromQuery] string companyId, CancellationToken token,
        [FromQuery] long cursor = 0, [FromQuery] int limit = 100)
    {
        if (cursor < 0 || limit is < 1 or > 500) return BadRequest(new { message = "Invalid cursor or limit." });
        var result = await jobs.ErrorsAsync(id, companyId, cursor, limit, token);
        return result is null ? NotFound() : Ok(result);
    }
}
