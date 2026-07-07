using Blocks.CaptchaDriver;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> extensions that wire the Blocks Captcha driver.
/// </summary>
public static class CaptchaDriverServiceExtension
{
    /// <summary>
    /// Registers the Blocks Captcha driver and its dependencies in the supplied <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">Service collection to register against.</param>
    public static void RegisterBlocksCaptchaService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CaptchaOptions>()
            .BindConfiguration(CaptchaOptions.SectionName);

        services.AddHttpClient();
        services.AddSingleton<IHttpClientService, HttpClientService>();

        services.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();

        services.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
        services.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
        services.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
        services.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
        services.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();
        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaDriverService, CaptchaDriverService>();

        services.AddSingleton<ICaptchaVerificationService, BlocksCaptchaVerificationService>();
        services.AddSingleton<ICaptchaVerificationService, ReCaptchaVerificationService>();
        services.AddSingleton<ICaptchaVerificationService, HCaptchaVerificationService>();
    }
}
