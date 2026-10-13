using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DescagaCompronanteSRI.Persistence;
using DescagaCompronanteSRI.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DescagaCompronanteSRI.ApiSecurity;

public enum ApiSecurityMode { Required, LocalDevelopment }
public sealed class ApiSecurityOptions
{
    public ApiSecurityMode Mode { get; set; } = ApiSecurityMode.Required;
    public Guid AdministratorId { get; set; }
    public string AdministratorHash { get; set; } = "";
    [Range(1, 365)] public int DefaultKeyLifetimeDays { get; set; } = 90;
    public bool HasAdministrator => AdministratorId != Guid.Empty && ValidHash(AdministratorHash);
    public static bool ValidHash(string value)
    {
        try { return Convert.FromBase64String(value).Length == 32; }
        catch (FormatException) { return false; }
    }
}

public static class ApiAccess
{
    public const string Scheme = "CompanyApiKey";
    public const string Header = "XApiKey";
    public const string CompanyPolicy = "CompanyAccess";
    public const string AdminPolicy = "ApiKeyAdministration";
    public const string CompanyClaim = "crawler:company";
    public const string KindClaim = "crawler:credentialKind";
    public const string CompanyKind = "company";
    public const string AdminKind = "administrator";
    public const string LocalKind = "localDevelopment";
    public static bool IsLocal(ApiSecurityOptions options, IHostEnvironment environment) =>
        options.Mode == ApiSecurityMode.LocalDevelopment && environment.IsDevelopment();
    public static bool CanAccess(ClaimsPrincipal user, string companyId) => user.HasClaim(KindClaim, LocalKind)
        || user.FindFirstValue(CompanyClaim) == companyId;
    public static string Company(ClaimsPrincipal user, string requestedCompany) => user.FindFirstValue(CompanyClaim) ?? requestedCompany;
}

public static class ApiSecurityRegistration
{
    public static void AddSecurityEnvironmentFile(ConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        var local = Path.Combine(environment.ContentRootPath, ".env.security");
        var parent = Path.Combine(Path.GetDirectoryName(environment.ContentRootPath) ?? environment.ContentRootPath, ".env.security");
        var file = File.Exists(local) ? local : File.Exists(parent) ? parent : null;
        if (file is null) return;
        try
        {
            var values = DotNetEnv.Env.NoEnvVars().Load(file).Where(x => x.Key.StartsWith("ApiSecurity__", StringComparison.Ordinal))
                .ToDictionary(x => x.Key.Replace("__", ":"), x => (string?)x.Value);
            var index = configuration.Sources.Select((s, i) => (s, i))
                .Where(x => x.s is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource).Select(x => x.i).DefaultIfEmpty(-1).Max();
            configuration.Sources.Insert(index + 1, new MemoryConfigurationSource { InitialData = values });
        }
        catch { throw new InvalidOperationException("Unable to read .env.security. Check its syntax and permissions."); }
    }

    public static void AddApiSecurity(this IServiceCollection services, bool migrate)
    {
        var options = services.AddOptions<ApiSecurityOptions>().BindConfiguration("ApiSecurity");
        if (!migrate)
        {
            options.ValidateDataAnnotations().Validate(o => Enum.IsDefined(o.Mode), "ApiSecurity Mode is invalid.")
                .Validate<IHostEnvironment>((o, env) => o.Mode != ApiSecurityMode.LocalDevelopment || env.IsDevelopment(), "Local development access requires the Development environment.")
                .Validate(o => o.Mode != ApiSecurityMode.Required || o.HasAdministrator, "Required access needs an administrator ID and base64 SHA-256 hash.")
                .Validate(o => (o.AdministratorId == Guid.Empty && string.IsNullOrEmpty(o.AdministratorHash)) || o.HasAdministrator,
                    "Administrator configuration must contain both a valid ID and hash.").ValidateOnStart();
        }
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICompanyApiKeys>(provider => new CompanyApiKeys(provider.GetRequiredService<CrawlerDbContext>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<ApiSecurityOptions>>().Value.DefaultKeyLifetimeDays));
        services.AddAuthentication(ApiAccess.Scheme).AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiAccess.Scheme, _ => { });
        services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new AuthorizationPolicyBuilder(ApiAccess.Scheme).RequireAuthenticatedUser().Build();
            o.AddPolicy(ApiAccess.CompanyPolicy, p => p.AddAuthenticationSchemes(ApiAccess.Scheme).RequireAuthenticatedUser()
                .RequireClaim(ApiAccess.KindClaim, ApiAccess.CompanyKind, ApiAccess.LocalKind));
            o.AddPolicy(ApiAccess.AdminPolicy, p => p.AddAuthenticationSchemes(ApiAccess.Scheme).RequireAuthenticatedUser()
                .RequireClaim(ApiAccess.KindClaim, ApiAccess.AdminKind));
        });
        services.AddTransient<IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>, ConfigureSecurityControllers>();
    }
}

public sealed class ConfigureSecurityControllers(IOptions<ApiSecurityOptions> options, IHostEnvironment environment)
    : IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>
{
    public void Configure(Microsoft.AspNetCore.Mvc.MvcOptions settings) => settings.Conventions.Add(new IssuedConvention(ApiAccess.IsLocal(options.Value, environment)));
    private sealed class IssuedConvention(bool enabled) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            if (enabled) return;
            foreach (var controller in application.Controllers.Where(x => x.ControllerType == typeof(SriEmitidosController)).ToArray())
                application.Controllers.Remove(controller);
        }
    }
}

public sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory,
    UrlEncoder encoder, IOptions<ApiSecurityOptions> security, IHostEnvironment environment, ICompanyApiKeys keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private bool unavailable;
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiAccess.Header, out var values))
            return ApiAccess.IsLocal(security.Value, environment) ? Success(ApiAccess.LocalKind) : AuthenticateResult.NoResult();
        if (values.Count != 1 || !ApiKeyToken.TryParse(values[0] ?? "", out var id, out var hash)) return Rejected();
        if (security.Value.HasAdministrator && id == security.Value.AdministratorId)
            return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(security.Value.AdministratorHash))
                ? Success(ApiAccess.AdminKind, id) : Rejected();
        try
        {
            var identity = await keys.AuthenticateAsync(id, hash, Context.RequestAborted);
            return identity is null ? Rejected() : Success(ApiAccess.CompanyKind, identity.KeyId, identity.CompanyId);
        }
        catch (ApiKeyPersistenceUnavailableException)
        {
            unavailable = true;
            Logger.LogWarning(new EventId(1401, "ApiKeyVerificationUnavailable"), "API key verification unavailable.");
            return AuthenticateResult.Fail("Credential verification unavailable.");
        }
    }
    private AuthenticateResult Rejected()
    {
        Logger.LogDebug(new EventId(1400, "ApiKeyRejected"), "API key rejected.");
        return AuthenticateResult.Fail("Invalid API key.");
    }
    private AuthenticateResult Success(string kind, Guid? id = null, string? company = null)
    {
        Logger.LogDebug(new EventId(1404, "ApiKeyAuthenticated"), "API credential authenticated: {keyId}, kind {credentialKind}, company {companyId}.", id, kind, company);
        var claims = new List<Claim> { new(ApiAccess.KindClaim, kind) };
        if (id is { } keyId) claims.Add(new(ClaimTypes.NameIdentifier, keyId.ToString("N")));
        if (company is not null) claims.Add(new(ApiAccess.CompanyClaim, company));
        return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, ApiAccess.Scheme)), ApiAccess.Scheme));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = unavailable ? 503 : 401;
        return Response.WriteAsJsonAsync(new { code = unavailable ? "authenticationUnavailable" : "authenticationRequired",
            message = unavailable ? "Credential verification is temporarily unavailable." : "A valid API key is required." }, Context.RequestAborted);
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Context.User.HasClaim(ApiAccess.KindClaim, ApiAccess.LocalKind)) return HandleChallengeAsync(properties);
        Response.StatusCode = 403;
        return Response.WriteAsJsonAsync(new { code = "accessDenied", message = "The credential does not permit this operation." }, Context.RequestAborted);
    }
}

public sealed class ApiSecurityOperationFilter(IOptions<ApiSecurityOptions> options) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.Name == nameof(DescagaCompronanteSRI.Controllers.ReceivedDocumentsController.DownloadFile))
            operation.Responses["200"] = new OpenApiResponse { Description = "Registered document file", Content = new Dictionary<string, OpenApiMediaType>
                { ["application/xml"] = new() { Schema = new() { Type = "string", Format = "binary" } },
                    ["application/pdf"] = new() { Schema = new() { Type = "string", Format = "binary" } } } };
        var policies = context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>()
            .Concat(context.MethodInfo.DeclaringType!.GetCustomAttributes(true).OfType<AuthorizeAttribute>());
        if (!policies.Any(x => x.Policy == ApiAccess.AdminPolicy || (x.Policy == ApiAccess.CompanyPolicy && options.Value.Mode == ApiSecurityMode.Required))) return;
        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecurityScheme
            { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }] = [] }];
        operation.Responses.TryAdd("401", new() { Description = "A valid API key is required." });
        operation.Responses.TryAdd("403", new() { Description = "The credential does not permit this operation." });
        operation.Responses.TryAdd("503", new() { Description = "Credential verification or persistence is unavailable." });
    }
}
