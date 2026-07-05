using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Default <see cref="ICaptchaService"/> implementation. Validates incoming requests,
/// delegates verification to a provider-specific service, and maps results to response DTOs.
/// </summary>
public sealed class CaptchaService : ICaptchaService
{
    private readonly IValidator<SubmitCaptchaRequest> _submitCaptchaCommandValidator;
    private readonly ICaptchaProcessor _captchaProcessor;
    private readonly ICaptchaConfigurationService _configurationService;
    private readonly ILogger<CaptchaService> _logger;

    public CaptchaService(
        ICaptchaProcessor captchaProcessor,
        IValidator<SubmitCaptchaRequest> submitCaptchaCommandValidator,
        ILogger<CaptchaService> logger,
        ICaptchaConfigurationService configurationService)
    {
        _captchaProcessor = captchaProcessor;
        _submitCaptchaCommandValidator = submitCaptchaCommandValidator;
        _logger = logger;
        _configurationService = configurationService;
    }

    /// <inheritdoc />
    public async Task<SubmitCaptchaRequestResponse> SubmitCaptchaAsync(SubmitCaptchaRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationResult = await _submitCaptchaCommandValidator.ValidateAsync(command);
        if (!validationResult.IsValid)
        {
            return new SubmitCaptchaRequestResponse(validationResult);
        }

        var verificationCode = await _captchaProcessor.SubmitAndCreateVerificationCodeAsync(
            command.Id,
            command.HostName);

        return new SubmitCaptchaRequestResponse(validationResult)
        {
            VerificationCode = verificationCode,
            IsSuccess = true
        };
    }

    /// <inheritdoc />
    public async Task<VerifyCaptchaRequestResponse> VerifyCaptchaAsync(VerifyCaptchaRequest query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.VerificationCode))
        {
            return new VerifyCaptchaRequestResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "VerificationCode", "Verification code cannot be null or empty." }
                }
            };
        }

        var config = await _configurationService.GetByNameAsync(query.ConfigurationName);
        if (config is null)
        {
            _logger.LogWarning(
                "Captcha verification requested with unknown configuration {ConfigurationName}",
                query.ConfigurationName);
            return new VerifyCaptchaRequestResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Configuration", $"Configuration '{query.ConfigurationName}' was not found." }
                }
            };
        }

        var verificationResult = await _captchaProcessor.VerifyCaptchaAsync(
            config.Provider,
            query.VerificationCode);

        return verificationResult.ToVerifyCaptchaQueryResponse();
    }
}
