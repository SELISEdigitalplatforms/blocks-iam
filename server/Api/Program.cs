using Blocks.Genesis;
using Cloud.DomainService.Utilities;
using Authentication.DomainService.Utilities;
using Identifier.DomainService.Shared;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using CloudConfiguration.DomainService.Shared.Utilities;

var builder = WebApplication.CreateBuilder(args);
ApplicationConfigurations.ConfigureApiEnv(builder, args);

var serviceName = ResolveRequiredServiceName(builder.Configuration);
var vaultType = ApplicationConfigurations.ResolveVaultType();
Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, vaultType);
Console.WriteLine(secret.AllowedCorsOrigins);
var messageConfiguration = IdpConstants.GetMessageConfiguration(secret.MessageConnectionString);
messageConfiguration.ServiceName = serviceName;
ApplicationConfigurations.ConfigureServices(builder.Services, messageConfiguration);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
});

var services = builder.Services;
var apiRoutePrefix = builder.Configuration["ApiRouting:Prefix"];

services.AddHealthChecks();

ApplicationConfigurations.ConfigureApi(
    services,
    serviceName: serviceName,
    apiRoutePrefix: apiRoutePrefix);

var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);

ApplyFrontendRuntimeSettings(builder.Configuration, wwwrootPath);

services.RegisterAllServices();
services.AddApplicationServices();
services.AddCloudDomainServices();
services.AddCloudConfigurationServices();

var app = builder.Build();

// Configure API routes FIRST (before static files) so JSON endpoints return JSON not HTML
var normalizedApiRoutePrefix = ApplicationConfigurations.NormalizeApiRoutePrefixValue(apiRoutePrefix);
ApplicationConfigurations.ConfigureMiddleware(
    app,
    tenantValidationPrefixes: new[] { normalizedApiRoutePrefix });

// Register controller routes with /idp/v1 prefix for attribute-based routing
var apiGroup = app.MapGroup(normalizedApiRoutePrefix);
apiGroup.MapControllers();

// THEN serve static files
app.UseDefaultFiles();
app.UseStaticFiles();

// Finally, fallback to index.html for React SPA routing (non-API routes)
var indexHtml = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(indexHtml))
{
    app.MapFallbackToFile("/index.html");
}

await app.RunAsync();

static void ApplyFrontendRuntimeSettings(IConfiguration configuration, string webRootPath)
{

    static string? ResolveEnvOrConfig(IConfiguration config, string envKey, string configKey) =>
        Environment.GetEnvironmentVariable(envKey) ?? config[configKey];

    var replacements = new Dictionary<string, string?>
    {
        ["__BLOCKS_API_BASE_URL__"] = ResolveEnvOrConfig(configuration, "BLOCKS_API_BASE_URL", "FrontendRuntime:BLOCKS_API_BASE_URL"),
        ["__BLOCKS_X_BLOCKS_KEY__"] = ResolveEnvOrConfig(configuration, "BLOCKS_X_BLOCKS_KEY", "FrontendRuntime:BLOCKS_X_BLOCKS_KEY"),
        ["__BLOCKS_GOOGLE_SITE_KEY__"] = ResolveEnvOrConfig(configuration, "BLOCKS_GOOGLE_SITE_KEY", "FrontendRuntime:BLOCKS_GOOGLE_SITE_KEY"),
        ["__BLOCKS_CONSTRUCT_URL__"] = ResolveEnvOrConfig(configuration, "BLOCKS_CONSTRUCT_URL", "FrontendRuntime:BLOCKS_CONSTRUCT_URL"),
        ["__BLOCKS_OIDC_CLIENT_ID__"] = ResolveEnvOrConfig(configuration, "BLOCKS_OIDC_CLIENT_ID", "FrontendRuntime:BLOCKS_OIDC_CLIENT_ID"),
    };

    var files = Directory.EnumerateFiles(webRootPath, "*", SearchOption.AllDirectories)
        .Where(path =>
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
        });

    foreach (var filePath in files)
    {
        var content = File.ReadAllText(filePath);
        var updated = content;

        foreach (var (token, value) in replacements)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                updated = updated.Replace(token, value, StringComparison.Ordinal);
            }
        }

        if (!ReferenceEquals(content, updated) && !content.Equals(updated, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, updated);
        }
    }
}

static string ResolveRequiredServiceName(IConfiguration configuration)
{
    var serviceName = Environment.GetEnvironmentVariable("ServiceName") ?? configuration["ServiceName"];
    if (string.IsNullOrWhiteSpace(serviceName))
    {
        throw new InvalidOperationException("Missing required ServiceName configuration.");
    }

    return serviceName;
}
