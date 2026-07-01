using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Blocks.CaptchaDriver
{
    public class CaptchaService : ICaptchaService
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

        public async Task<SubmitCaptchaRequestResponse> SubmitCaptchaAsync(SubmitCaptchaRequest command)
        {
            var validationResult = await _submitCaptchaCommandValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                return new SubmitCaptchaRequestResponse(validationResult);
            }

            var verificationCode = await _captchaProcessor.SubmitAndCreateVerificationCodeAsync(command.Id);

            return new SubmitCaptchaRequestResponse(validationResult)
            {
                VerificationCode = verificationCode,
                IsSuccess = true
            };
        }

        public async Task<VerifyCaptchaRequestResponse> VerifyCaptchaAsync(VerifyCaptchaRequest query)
        {
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

            if (config == null)
            {
                return new VerifyCaptchaRequestResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "Configuration Provider", "Configuration Provider is not found." }
                    }
                };
            }

            var verificationResult = await _captchaProcessor.VerifyCaptchaAsync(config.Provider, query.VerificationCode);

            return verificationResult.ToVerifyCaptchaQueryResponse();
        }
    }
}