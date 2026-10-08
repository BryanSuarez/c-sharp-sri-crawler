using System.ComponentModel.DataAnnotations;

namespace DescagaCompronanteSRI.Validation;

public sealed class DocumentValidationOptions
{
    [Range(1, 20 * 1024 * 1024)] public int MaxXmlBytes { get; set; } = 20 * 1024 * 1024;
    [Range(1, int.MaxValue)] public int MaxPdfBytes { get; set; } = 20 * 1024 * 1024;
    [Range(1, 100000)] public int MaxPdfPages { get; set; } = 1000;
    public string PortalTimeZone { get; set; } = "America/Guayaquil";
    public static bool IsTimeZoneValid(string zone)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); return true; }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return false; }
    }
}
