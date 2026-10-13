using DescagaCompronanteSRI.ApiSecurity;
using DescagaCompronanteSRI.Tests.Storage;
using Microsoft.Extensions.Configuration;

namespace DescagaCompronanteSRI.Tests.Security;

public sealed class SecurityConfigurationTests
{
    [Fact]
    public void DevelopmentEnvironmentFileHasLowerPriorityThanExistingOverridesAndDoesNotSetProcessVariables()
    {
        using var environment = new StorageTestEnvironment();
        var before = Environment.GetEnvironmentVariable("ApiSecurity__DefaultKeyLifetimeDays");
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.security"), "ApiSecurity__Mode=localDevelopment\nApiSecurity__DefaultKeyLifetimeDays=15\nIGNORED_OPTION=ignored\n");
        var config = new ConfigurationManager(); config.AddInMemoryCollection(new Dictionary<string, string?> { ["ApiSecurity:DefaultKeyLifetimeDays"] = "30" });
        ApiSecurityRegistration.AddSecurityEnvironmentFile(config, environment);
        Assert.Equal("30", config["ApiSecurity:DefaultKeyLifetimeDays"]); Assert.Equal("localDevelopment", config["ApiSecurity:Mode"]);
        Assert.Null(config["IGNORED_OPTION"]); Assert.Equal(before, Environment.GetEnvironmentVariable("ApiSecurity__DefaultKeyLifetimeDays"));
        environment.EnvironmentName = "Production";
        var production = new ConfigurationManager(); ApiSecurityRegistration.AddSecurityEnvironmentFile(production, environment);
        Assert.Null(production["ApiSecurity:Mode"]);
    }

    [Fact]
    public void ParentFallbackLoadsOnlyWhenTheProjectFileIsAbsent()
    {
        using var environment = new StorageTestEnvironment(); var parent = environment.ContentRootPath;
        File.WriteAllText(Path.Combine(parent, ".env.security"), "ApiSecurity__DefaultKeyLifetimeDays=20\n");
        environment.ContentRootPath = Path.Combine(parent, "project"); Directory.CreateDirectory(environment.ContentRootPath);
        var config = new ConfigurationManager(); ApiSecurityRegistration.AddSecurityEnvironmentFile(config, environment);
        Assert.Equal("20", config["ApiSecurity:DefaultKeyLifetimeDays"]);
        File.WriteAllText(Path.Combine(environment.ContentRootPath, ".env.security"), "ApiSecurity__DefaultKeyLifetimeDays=10\n");
        var local = new ConfigurationManager(); ApiSecurityRegistration.AddSecurityEnvironmentFile(local, environment);
        Assert.Equal("10", local["ApiSecurity:DefaultKeyLifetimeDays"]);
        environment.ContentRootPath = parent;
    }
}
