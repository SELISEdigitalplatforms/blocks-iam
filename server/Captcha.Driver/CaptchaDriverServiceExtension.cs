using Blocks.CaptchaDriver;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection;

public static class CaptchaDriverServiceExtension
{
    public static void RegisterBlocksCaptchaService(this IServiceCollection services)
    {
        services.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();

        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
        services.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
        services.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
        services.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();
        services.AddSingleton<ICaptchaDriverService, CaptchaDriverService>();
        services.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
        services.AddSingleton<IHttpClientService, HttpClientService>();

        services.AddSingleton<ReCaptchaVerificationService>();
        services.AddSingleton<HCaptchaVerificationService>();
        services.AddSingleton<BlocksCaptchaVerificationService>();
    }
}