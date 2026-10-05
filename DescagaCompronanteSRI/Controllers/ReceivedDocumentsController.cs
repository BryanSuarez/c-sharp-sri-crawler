using System.Globalization;
using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Requests;
using DescagaCompronanteSRI.Models.Responses;
using DescagaCompronanteSRI.Validation;
using Microsoft.AspNetCore.Mvc;
namespace DescagaCompronanteSRI.Controllers;

[ApiController]
[Route("api/received-documents")]
public sealed class ReceivedDocumentsController(IReceivedDocumentsService service) : ControllerBase
{
    /// <summary>Downloads documents from the currently visible received-documents page.</summary>
    [HttpPost("query")]
    [ProducesResponseType(typeof(ReceivedDocumentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ReceivedDocumentsResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query([FromBody] ReceivedDocumentsQueryRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!SriRequestValidator.TryNormalizeTaxpayerId(request.User, out var taxpayerId, out _))
            ModelState.AddModelError("user", "User must contain 10 or 13 digits.");
        if (!SriRequestValidator.TryValidateReceivedDate(
                request.Year!.Value.ToString(CultureInfo.InvariantCulture), request.Month!.Value, request.Day!.Value, out _, out _))
            ModelState.AddModelError("date", "The requested date is invalid. Day 0 selects the entire month.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var query = new ReceivedDocumentsQuery
        {
            User = taxpayerId,
            AdditionalUser = request.AdditionalUser?.Trim(),
            Password = request.Password,
            Year = request.Year.Value,
            Month = request.Month.Value,
            Day = request.Day.Value,
            DocumentType = request.DocumentType!.Value,
            DownloadFormat = request.DownloadFormat!.Value
        };
        var result = await service.QueryAsync(query);
        return result.QuerySucceeded ? Ok(result) : StatusCode(500, result);
    }
}
