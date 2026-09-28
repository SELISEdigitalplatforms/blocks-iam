using Iam.DomainService.Utilities;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http.Features;
using SeliseBlocks.ConfigurationDriver;


var builder = WebApplication.CreateBuilder(args);
ApplicationConfigurations.ConfigureApiEnv(builder, args);

// Register IHttpContextAccessor for static DomainResolver
builder.Services.AddHttpContextAccessor();

var serviceName = ResolveRequiredServiceName(builder.Configuration);
var vaultType = ApplicationConfigurations.ResolveVaultType();
Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, vaultType);

Console.WriteLine(secret.AllowedCorsOrigins);

var messageConfiguration = IdpConstants.GetMessageConfiguration(secret.MessageConnectionString);
messageConfiguration.ServiceName = serviceName;
ApplicationConfigurations.ConfigureServices(builder.Services, messageConfiguration);
builder.Configuration.AddMongoDbConfiguration(options =>
{
    options.ConnectionString = secret.DatabaseConnectionString;
    options.DatabaseName = secret.RootDatabaseName;
    options.CollectionName = "Secrets";
    options.SecretKey = "blocks-secret-iam";
});
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


var app = builder.Build();

// Configure DomainResolver with IHttpContextAccessor instance
DomainResolver.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

// Browser-facing security headers (ZAP DAST bar: 0 alerts). Use OnStarting so
// Genesis/OIDC can still append Set-Cookie; never write Cache-Control on cookie-
// issuing OIDC/token paths; softer Referrer-Policy there for cross-host redirects.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path.Value ?? string.Empty;
        var isOidcOrTokenPath =
            path.StartsWith("/api/oidc", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/oidc", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/idp", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/token", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/connect/token", StringComparison.OrdinalIgnoreCase);

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // IAM OIDC token exchange on PR previews fails when the SPA sends
        // Referrer-Policy: no-referrer (fetch to /api/oidc/token loses Referer).
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // External /runtime-config.js + /theme-init.js (no inline script). Explicit
        // hosts avoid CSP wildcards that ZAP flags (blocks-os pattern).
        if (!headers.ContainsKey("Content-Security-Policy"))
        {
            var connectHosts =
                "https://dev-iam.blocksdevelopers.com " +
                "https://dev-api.blocksdevelopers.com " +
                "https://dev-construct.blocksdevelopers.com " +
                "https://dev-localization.blocksdevelopers.com " +
                "https://dev-agents.blocksdevelopers.com " +
                "https://dev-data.blocksdevelopers.com " +
                "https://dev-utilities.blocksdevelopers.com " +
                "https://dev-logic.blocksdevelopers.com " +
                "wss://dev-logic.blocksdevelopers.com " +
                "https://dev-monitor.blocksdevelopers.com " +
                "https://dev-release.blocksdevelopers.com " +
                "https://dev-studio.blocksdevelopers.com " +
                "https://dev-os.blocksdevelopers.com " +
                "https://blocksdev.blob.core.windows.net " +
                "https://api.rollbar.com " +
                "https://code.selise.biz";
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: blob: https://blocksdev.blob.core.windows.net https://az-cdn.selise.biz; " +
                "font-src 'self' data:; " +
                "connect-src 'self' " + connectHosts + "; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "object-src 'none'; " +
                "form-action 'self' https://dev-iam.blocksdevelopers.com https://dev-os.blocksdevelopers.com";
        }

        // Cache-Control: never on OIDC/token (Set-Cookie / auth responses).
        if (!isOidcOrTokenPath && !headers.ContainsKey("Cache-Control"))
        {
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                || path == "/"
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("runtime-config.js", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("theme-init.js", StringComparison.OrdinalIgnoreCase)
                || !Path.HasExtension(path))
            {
                headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                headers["Pragma"] = "no-cache";
            }
            else if (path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
            {
                headers["Cache-Control"] = "public, max-age=31536000, immutable";
            }
        }

        // OIDC authorize sometimes returns empty/redirect without Content-Type; ZAP flags it.
        if ((path.StartsWith("/api/oidc/authorize", StringComparison.OrdinalIgnoreCase)
             || path.StartsWith("/oidc/authorize", StringComparison.OrdinalIgnoreCase))
            && !headers.ContainsKey("Content-Type")
            && context.Response.StatusCode < 300)
        {
            headers["Content-Type"] = "text/html; charset=utf-8";
        }

        return Task.CompletedTask;
    });

    await next();
});

// Configure API routes FIRST (before static files) so JSON endpoints return JSON not HTML
var normalizedApiRoutePrefix = ApplicationConfigurations.NormalizeApiRoutePrefixValue(apiRoutePrefix);
ApplicationConfigurations.ConfigureMiddleware(
    app,
    tenantValidationPrefixes: new[] { normalizedApiRoutePrefix });

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
    // ACTIVE path: read frontend runtime values from the "FrontendRuntime" section in
    // appsettings.{Environment}.json. Standard .NET config layering still applies, so
    // env vars named "FrontendRuntime__BLOCKS_*" override individual keys at deploy time.
    var section = configuration.GetSection("FrontendRuntime");
    var replacements = new Dictionary<string, string?>
    {
        ["__BLOCKS_X_BLOCKS_KEY__"] = section["BLOCKS_X_BLOCKS_KEY"],
        ["__BLOCKS_GOOGLE_SITE_KEY__"] = section["BLOCKS_GOOGLE_SITE_KEY"],
        ["__BLOCKS_CONSTRUCT_URL__"] = section["BLOCKS_CONSTRUCT_URL"],
        ["__BLOCKS_GITHUB_SSO_CLIENT_ID__"] = section["BLOCKS_GITHUB_SSO_CLIENT_ID"],
        ["__BLOCKS_IAM_BASE_URL__"] = section["BLOCKS_IAM_BASE_URL"],
        ["__BLOCKS_OIDC_CLIENT_ID__"] = section["BLOCKS_OIDC_CLIENT_ID"],
        ["__BLOCKS_BASE_DOMAIN__"] = section["BLOCKS_BASE_DOMAIN"],
        ["__BLOCKS_IAM_CALLBACK_URL__"] = section["BLOCKS_IAM_CALLBACK_URL"],
        ["__BLOCKS_LOCALIZATION_BASE_URL__"] = section["BLOCKS_LOCALIZATION_BASE_URL"],
        ["__BLOCKS_LOCALIZATION_CALLBACK_URL__"] = section["BLOCKS_LOCALIZATION_CALLBACK_URL"],
        ["__BLOCKS_AGENTS_BASE_URL__"] = section["BLOCKS_AGENTS_BASE_URL"],
        ["__BLOCKS_AGENTS_CALLBACK_URL__"] = section["BLOCKS_AGENTS_CALLBACK_URL"],
        ["__BLOCKS_DATA_BASE_URL__"] = section["BLOCKS_DATA_BASE_URL"],
        ["__BLOCKS_DATA_CALLBACK_URL__"] = section["BLOCKS_DATA_CALLBACK_URL"],
        ["__BLOCKS_OS_BASE_URL__"] = section["BLOCKS_OS_BASE_URL"],
        ["__BLOCKS_OS_CALLBACK_URL__"] = section["BLOCKS_OS_CALLBACK_URL"],
        ["__BLOCKS_UTILITIES_BASE_URL__"] = section["BLOCKS_UTILITIES_BASE_URL"],
        ["__BLOCKS_UTILITIES_CALLBACK_URL__"] = section["BLOCKS_UTILITIES_CALLBACK_URL"],
        ["__BLOCKS_LOGIC_BASE_URL__"] = section["BLOCKS_LOGIC_BASE_URL"],
        ["__BLOCKS_LOGIC_CALLBACK_URL__"] = section["BLOCKS_LOGIC_CALLBACK_URL"],
        ["__BLOCKS_MONITOR_BASE_URL__"] = section["BLOCKS_MONITOR_BASE_URL"],
        ["__BLOCKS_MONITOR_CALLBACK_URL__"] = section["BLOCKS_MONITOR_CALLBACK_URL"],
        ["__BLOCKS_RELEASE_BASE_URL__"] = section["BLOCKS_RELEASE_BASE_URL"],
        ["__BLOCKS_RELEASE_CALLBACK_URL__"] = section["BLOCKS_RELEASE_CALLBACK_URL"],
        ["__BLOCKS_STUDIO_BASE_URL__"] = section["BLOCKS_STUDIO_BASE_URL"],
        ["__BLOCKS_STUDIO_CALLBACK_URL__"] = section["BLOCKS_STUDIO_CALLBACK_URL"],
        ["__BLOCKS_DATA_CLIENT_ID__"] = section["BLOCKS_DATA_CLIENT_ID"],
        ["__BLOCKS_IAM_CLIENT_ID__"] = section["BLOCKS_IAM_CLIENT_ID"],
        ["__BLOCKS_LOCALIZATION_CLIENT_ID__"] = section["BLOCKS_LOCALIZATION_CLIENT_ID"],
        ["__BLOCKS_AGENTS_CLIENT_ID__"] = section["BLOCKS_AGENTS_CLIENT_ID"],
        ["__BLOCKS_OS_CLIENT_ID__"] = section["BLOCKS_OS_CLIENT_ID"],
        ["__BLOCKS_UTILITIES_CLIENT_ID__"] = section["BLOCKS_UTILITIES_CLIENT_ID"],
        ["__BLOCKS_LOGIC_CLIENT_ID__"] = section["BLOCKS_LOGIC_CLIENT_ID"],
        ["__BLOCKS_RELEASE_CLIENT_ID__"] = section["BLOCKS_RELEASE_CLIENT_ID"],
        ["__BLOCKS_MONITOR_CLIENT_ID__"] = section["BLOCKS_MONITOR_CLIENT_ID"],
        ["__BLOCKS_STUDIO_CLIENT_ID__"] = section["BLOCKS_STUDIO_CLIENT_ID"],
        ["__BLOCKS_ROLLBAR_CLIENT_TOKEN__"] = section["BLOCKS_ROLLBAR_CLIENT_TOKEN"],
        ["__BLOCKS_ROLLBAR_ENV__"] = section["BLOCKS_ROLLBAR_ENV"],
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
