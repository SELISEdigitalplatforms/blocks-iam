using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentValidation;
using MongoDB.Driver;
using Authentication.DomainService.Utilities;

namespace Iam.DomainService.Accounts
{
    public class RecoveryUserRequestValidator : AbstractValidator<RecoveryUserRequest>
    {
        private readonly ICaptchaService _captchaService;
        private readonly IDbContextProvider _dbContextProvider;

        public RecoveryUserRequestValidator(ICaptchaService captchaService,
                                            IDbContextProvider dbContextProvider)
        {
            _captchaService = captchaService;
            _dbContextProvider = dbContextProvider;

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

        public async Task<CaptchaConfiguration?> GetCaptchaConfig()
        {
            var collection = _dbContextProvider.GetCollection<Secret>("Secrets");
            var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

            var secrets = await (await collection.FindAsync(filter)).ToListAsync();

            var configuration = secrets.Select(IdpConstants.MapToCaptchaConfiguration).FirstOrDefault(configuration => configuration is { IsEnable: true });
            return configuration;
        }
    }
}
