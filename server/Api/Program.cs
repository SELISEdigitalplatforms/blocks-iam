using BlocksTemplate.Api;
using Blocks.Genesis;
using Cloud.DomainService.Utilities;
using DomainService.Utilities;
using DomainService.Shared;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Cloud.LmtService.Utilities;

var serviceName = "blocks-idp-api";
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, VaultType.Azure);
var builder = WebApplication.CreateBuilder(args);


ApplicationConfigurations.ConfigureApiEnv(builder, args);
ApplicationConfigurations.ConfigureServices(builder.Services, IdpConstants.GetMessageConfiguration(secret.MessageConnectionString));

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
});

var services = builder.Services;

services.AddHealthChecks();

ApplicationConfigurations.ConfigureApi(services);

builder.Services.Configure<MvcOptions>(options =>
{
    options.Conventions.Insert(0, new GlobalApiRoutePrefixConvention("api"));
});

var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);

services.RegisterAllServices();
services.AddApplicationServices();
services.AddCloudDomainServices();
services.AddCloudLmtServices();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var indexHtml = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
if (File.Exists(indexHtml))
{
    app.MapFallbackToFile("/index.html");
}

ApplicationConfigurations.ConfigureMiddleware(app);

await app.RunAsync();
