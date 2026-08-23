using Blocks.CaptchaDriver;
using FluentValidation;

namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequestValidator : AbstractValidator<RecoveryUserRequest>
    {
        private readonly ICaptchaService _captchaService;
        private readonly ICaptchaConfigurationService _captchaConfigurationService;

        public RecoveryUserRequestValidator(ICaptchaService captchaService,
                                            ICaptchaConfigurationService captchaConfigurationService)
        {
            _captchaService = captchaService;
            _captchaConfigurationService = captchaConfigurationService;

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Invalid email format.");

            RuleFor(x => x.CaptchaCode)
                   .Cascade(CascadeMode.Stop)
                   .MustAsync(MustMatchCaptcha).WithMessage("Captcha doesn't match")
                   .When(x => !string.IsNullOrWhiteSpace(x.CaptchaCode));
        }

        private async Task<bool> MustMatchCaptcha(string captchaCode, CancellationToken cancellationToken)
        {
            var configurationName = (await GetCaptchaConfig())?.Provider ?? "";
            var verifyCaptchaQueryResponse = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest { VerificationCode = captchaCode, ConfigurationName = configurationName });

            return verifyCaptchaQueryResponse.Verified;
        }

        /// <summary>
        /// Reads the active configuration through the driver, which prefers the blocks-os
        /// key/value store and falls back to the legacy secret document.
        /// </summary>
        public async Task<CaptchaConfiguration?> GetCaptchaConfig()
        {
            return await _captchaConfigurationService.GetCaptchaConfigurationAsync();
        }
    }
}
