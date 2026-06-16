using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentValidation;
using MongoDB.Driver;

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

        public async Task<CaptchaConfiguration> GetCaptchaConfig()
        {
            var collection = _dbContextProvider.GetCollection<Secret>("Secrets");
            var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

            var secrets = await (await collection.FindAsync(filter)).ToListAsync();

            return secrets.Select(MapToCaptchaConfiguration).FirstOrDefault(configuration => configuration is { IsEnable: true });
        }

        private static CaptchaConfiguration MapToCaptchaConfiguration(Secret secret)
        {
            if (secret?.KeyPairs is not { } values)
            {
                return null;
            }

            return new CaptchaConfiguration
            {
                CaptchaKey = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaKey),
                CaptchaSecret = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaSecret),
                Provider = values.GetValueOrDefault(CaptchaSecretKeys.Provider),
                CaptchaGenerator = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator),
                IsEnable = bool.TryParse(values.GetValueOrDefault(CaptchaSecretKeys.IsEnable), out var isEnable) && isEnable
            };
        }

        // Returns the first non-empty value among the candidate keys (case-insensitive lookup).
        private static string? Pick(Dictionary<string, string?> values, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        // isEnable is stored as a string and may come in different shapes
        // ("true"/"True"/"TRUE"/" true ", and occasionally "1"/"yes"/"on").
        private static bool IsEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return bool.TryParse(normalized, out var parsed)
                ? parsed
                : normalized is "1"
                  || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase)
                  || normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }
}
