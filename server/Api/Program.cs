using BlocksTemplate.Api;
using BlocksTemplate.DomainService;
using Blocks.Genesis;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var serviceName = "blocks-template-api";
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, VaultType.Azure);
var builder = WebApplication.CreateBuilder(args);


ApplicationConfigurations.ConfigureApiEnv(builder, args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
});

var services = builder.Services;

services.AddHealthChecks();

builder.Services.AddDomainServices();
builder.Services.AddFluentValidationAutoValidation();
ApplicationConfigurations.ConfigureServices(services, new MessageConfiguration { });
ApplicationConfigurations.ConfigureApi(services);

builder.Services.Configure<MvcOptions>(options =>
{
    options.Conventions.Insert(0, new GlobalApiRoutePrefixConvention("api"));
});

var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);

var app = builder.Build();

// Prepare index.html with runtime environment variables injected (once at startup)
var indexHtmlPath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
byte[]? injectedHtmlBytes = null;
if (File.Exists(indexHtmlPath))
{
    var runtimeEnvVars = new Dictionary<string, string?>
    {
        ["BLOCKS_X_BLOCKS_KEY"] = Environment.GetEnvironmentVariable("BLOCKS_X_BLOCKS_KEY"),
    };
    var envJson = System.Text.Json.JsonSerializer.Serialize(runtimeEnvVars);
    var envScript = $"<script>window.__ENV__={envJson};</script>";
    var originalHtml = File.ReadAllText(indexHtmlPath);
    var injectedHtml = originalHtml.Replace("<!--__ENV_PLACEHOLDER__-->", envScript);
    injectedHtmlBytes = System.Text.Encoding.UTF8.GetBytes(injectedHtml);
}

if (injectedHtmlBytes != null)
{
    // Intercept "/" and "/index.html" before static files to serve env-injected version
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "";
        if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = injectedHtmlBytes.Length;
            await context.Response.Body.WriteAsync(injectedHtmlBytes);
            return;
        }
        await next();
    });
}

app.UseStaticFiles();

if (injectedHtmlBytes != null)
{
    // SPA fallback — serves injected index.html for client-side routes
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = injectedHtmlBytes.Length;
        await context.Response.Body.WriteAsync(injectedHtmlBytes);
    });
}

ApplicationConfigurations.ConfigureMiddleware(app);

try
{
    await app.RunAsync();
}
catch (ObjectDisposedException ex) when (IsGenesisMongoTraceExporterShutdownRace(ex))
{
    // SeliseBlocks.Genesis OpenTelemetry MongoDBTraceExporter: flush can run after internal semaphore disposal on Ctrl+C.
    Console.WriteLine("MongoDBTraceExporter shutdown race detected. Ignoring...");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    throw;
}

static bool IsGenesisMongoTraceExporterShutdownRace(ObjectDisposedException ex) =>
    ex.StackTrace?.Contains("MongoDBTraceExporter", StringComparison.Ordinal) == true;
