using DescagaCompronanteSRI.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DescagaCompronanteSRI.Tests.Validation;

public sealed class DocumentValidationConfigurationTests
{
    [Theory]
    [InlineData("PortalTimeZone", "Invalid/Unknown")]
    [InlineData("MaxXmlBytes", "20971521")]
    [InlineData("MaxPdfBytes", "0")]
    [InlineData("MaxPdfPages", "0")]
    public void Startup_RejectsInvalidValidationOptions(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExtractionJobs:ConnectionString"] = "Host=localhost;Database=unused;Username=test;Password=test-only",
            ["ExtractionJobs:EncryptionKey"] = Convert.ToBase64String(new byte[32]),
            [$"DocumentValidation:{key}"] = value
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddExtractionJobs(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }
}
