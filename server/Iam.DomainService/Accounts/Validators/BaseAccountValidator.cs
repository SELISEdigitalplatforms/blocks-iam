using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;

namespace Iam.DomainService.Accounts
{
    public class BaseAccountValidator : PasswordValidator<BaseAccountRequest>
    {
        private readonly ICacheClient _cacheClient;
        private readonly ICaptchaService _captchaService;
        private readonly ICaptchaConfigurationService _captchaConfigurationService;


        public BaseAccountValidator(ICacheClient cacheClient,
                                    IIamConfigurationRepository configurationRepository,
                                    IIdentityAccessManagementRepository identityAccessManagementRepository,
                                    ICaptchaService captchaService,
                                    ICaptchaConfigurationService captchaConfigurationService)
            : base(identityAccessManagementRepository, configurationRepository)
        {
            _cacheClient = cacheClient;
            _captchaService = captchaService;
            _captchaConfigurationService = captchaConfigurationService;

            RuleFor(u => u.Code)
                .Cascade(CascadeMode.Stop)
                .NotNull().NotEmpty()
                .MustAsync(BeTheLatestCodeForThisPurpose).WithMessage("The code has expired. Please request a new one to continue");

            RuleFor(u => u.Password)
                .Cascade(CascadeMode.Stop)
                .MustAsync(BeAStrongPassword)
                .WithMessage("Does not meet project's password requirements")
                .MustAsync(CheckBlackListPassword)
                .WithMessage("This password can not be used.")
                .When(x => !string.IsNullOrWhiteSpace(x.Password));

            RuleFor(x => x.CaptchaCode)
               .Cascade(CascadeMode.Stop)
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

        // A reset code only resets, an activation code only activates, and only the user's latest
        // one works. Any other request type has no purpose and is refused.
        private async Task<bool> BeTheLatestCodeForThisPurpose(BaseAccountRequest request, string code, CancellationToken cancellationToken)
        {
            var purpose = request switch
            {
                ResetPasswordRequest => AccountCodes.Reset,
                ActivateUserRequest => AccountCodes.Activation,
                _ => null
            };
            if (purpose == null)
            {
                return false;
            }

            var userId = await AccountCodes.ResolveAsync(_cacheClient, purpose, code);
            if (userId == null)
            {
                return false;
            }

            if (purpose == AccountCodes.Reset)
            {
                return true;
            }

            // An account an administrator blocked cannot activate itself.
            var user = await _identityAccessManagementRepository.GetUserByIdAsync(userId);
            return user != null && !AccountCodes.IsBlocked(user);
        }

        /// <summary>
        /// Reads the active configuration through the driver, which prefers the blocks-os
        /// key/value store and falls back to the legacy secret document.
        /// </summary>
        private async Task<CaptchaConfiguration?> GetCaptchaConfig()
        {
            return await _captchaConfigurationService.GetCaptchaConfigurationAsync();
        }
    }
}
