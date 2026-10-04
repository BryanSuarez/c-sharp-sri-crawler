using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Requests;
using DescagaCompronanteSRI.Validation;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class ConsultaComprobantesController : ControllerBase
{
    private readonly IReceivedDocumentsService _service;
    private readonly IWebHostEnvironment _env;

    public ConsultaComprobantesController(
        IReceivedDocumentsService service,
        IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    [HttpPost("consultar")]
    public async Task<IActionResult> Consultar([FromBody] ReceivedDocumentsQueryRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!SriRequestValidator.TryNormalizeTaxpayerId(request.User, out var taxpayerId, out var userError))
            ModelState.AddModelError("usuario", userError);

        if (!SriRequestValidator.TryValidateReceivedDate(
                request.Year,
                request.Month,
                request.Day,
                out _,
                out var dateError))
            ModelState.AddModelError("fecha", dateError);

        if (!SriRequestValidator.IsValidDocumentTypeCode(request.DocumentType))
            ModelState.AddModelError("comprobante", "El campo comprobante no tiene un tipo permitido.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string destination = Path.Combine(_env.WebRootPath, "recibidos", taxpayerId);

        var query = new ReceivedDocumentsQuery
        {
            User = taxpayerId,
            AdditionalUser = request.AdditionalUser?.Trim(),
            Password = request.Password,
            Day = request.Day,
            Year = request.Year,
            Month = request.Month,
            DocumentType = SriRequestValidator.ToDocumentType(request.DocumentType),
            DownloadXml = request.DownloadXml
        };

        try
        {
            var result = await _service.QueryAsync(query, destination);

            return result is null
                ? StatusCode(500, new { mensaje = "Consulta fallida. Verifique credenciales o intente de nuevo." })
                : Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = "Error interno", detalle = ex.Message });
        }
    }
}
