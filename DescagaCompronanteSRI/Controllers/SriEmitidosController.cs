using DescagaCompronanteSRI.Contracts;
using DescagaCompronanteSRI.Models.Dtos;
using DescagaCompronanteSRI.Models.Requests;
using DescagaCompronanteSRI.Validation;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class SriEmitidosController : ControllerBase
{
    private readonly IIssuedDocumentsService _service;
    private readonly IWebHostEnvironment _env;

    public SriEmitidosController(
        IIssuedDocumentsService service,
        IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    // ════════════════════════════════════════════════════════════════════════════
    //   POST api/SriEmitidos/consultar
    // ════════════════════════════════════════════════════════════════════════════
    [HttpPost("consultar")]
    public async Task<IActionResult> Consultar([FromBody] IssuedDocumentsQueryRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!SriRequestValidator.TryNormalizeTaxpayerId(request.User, out var taxpayerId, out var userError))
            ModelState.AddModelError("usuario", userError);

        if (!SriRequestValidator.TryValidateIssuedDate(
                request.Year,
                request.Month,
                request.Day,
                out var dateError))
            ModelState.AddModelError("fecha", dateError);

        if (!SriRequestValidator.IsValidDocumentTypeCode(request.DocumentType))
            ModelState.AddModelError("comprobante", "El campo comprobante no tiene un tipo permitido.");

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Estructura: {WebRoot}/emitidos/{ruc}/
        string issuedRoot = Path.Combine(_env.WebRootPath, "emitidos");
        string taxpayerDirectory = Path.Combine(issuedRoot, taxpayerId);

        if (!Directory.Exists(issuedRoot)) Directory.CreateDirectory(issuedRoot);
        if (!Directory.Exists(taxpayerDirectory)) Directory.CreateDirectory(taxpayerDirectory);

        var query = new IssuedDocumentsQuery
        {
            User = taxpayerId,
            AdditionalUser = request.AdditionalUser?.Trim(),
            Password = request.Password,
            DocumentType = SriRequestValidator.ToDocumentType(request.DocumentType),
            Year = request.Year,
            Month = request.Month,
            Day = request.Day
        };

        try
        {
            var result = await _service.QueryAsync(query, taxpayerDirectory);

            if (result is null)
                return StatusCode(500, new
                {
                    mensaje = "Consulta fallida. Verifique credenciales o intente de nuevo."
                });

            if (result.TotalComprobantes == 0)
                return Ok(new
                {
                    result.Ruc,
                    result.RazonSocial,
                    total = 0,
                    mensaje = "No se encontraron comprobantes para la fecha indicada.",
                    comprobantes = Array.Empty<object>()
                });

            return Ok(new
            {
                result.Ruc,
                result.RazonSocial,
                total = result.TotalComprobantes,
                mensaje = $"{result.Comprobantes.Count}/{result.TotalComprobantes} " +
                               $"comprobante(s) descargado(s) en: emitidos/{taxpayerId}/",
                comprobantes = result.Comprobantes
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = "Error interno", detalle = ex.Message });
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //   GET api/SriEmitidos/descargar/{ruc}/{claveAcceso}
    //   Devuelve el PDF nombrado por clave de acceso
    // ════════════════════════════════════════════════════════════════════════════
    [HttpGet("descargar/{ruc}/{claveAcceso}")]
    public IActionResult Descargar(string ruc, string claveAcceso)
    {
        if (!SriRequestValidator.TryNormalizeTaxpayerId(ruc, out var taxpayerId, out var userError))
            return BadRequest(new { error = userError });

        if (!SriRequestValidator.IsValidAccessKey(claveAcceso))
            return BadRequest(new { error = "La claveAcceso debe tener exactamente 49 dígitos." });

        string pdfPath = Path.Combine(
            _env.WebRootPath, "emitidos", taxpayerId, $"{claveAcceso}.pdf");

        if (!System.IO.File.Exists(pdfPath))
            return NotFound(new { error = $"PDF no encontrado: emitidos/{taxpayerId}/{claveAcceso}.pdf" });

        byte[] bytes = System.IO.File.ReadAllBytes(pdfPath);
        return File(bytes, "application/pdf", $"{claveAcceso}.pdf");
    }
}
