using Authentication.DomainService.OAuth;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Decides whether the OIDC login must challenge the user with a captcha and produces the
    /// appropriate result envelope (pass / require with config key). Owns the small outcome enum
    /// and result DTO that used to live as nested types inside <c>AuthorizationFlowService</c>.
    /// </summary>
    public sealed class OidcCaptchaEvaluator
    {
        private readonly ICaptchaEvaluator _captchaEvaluator;

        public OidcCaptchaEvaluator(ICaptchaEvaluator captchaEvaluator)
        {
            _captchaEvaluator = captchaEvaluator;
        }

        public async Task<OidcCaptchaEvaluation> EvaluateAsync(User user, string? captchaCode)
        {
            if (!CaptchaGate.IsCaptchaRequired(user))
            {
                return OidcCaptchaEvaluation.Pass();
            }

            var captchaConfiguration = await _captchaEvaluator.GetConfigurationAsync();
            if (captchaConfiguration == null || !captchaConfiguration.IsEnable)
            {
                return OidcCaptchaEvaluation.Pass();
            }

            if (string.IsNullOrWhiteSpace(captchaCode))
            {
                return OidcCaptchaEvaluation.Require(OAuthError.CaptchaEnabled, "Captcha verification is required", captchaConfiguration.CaptchaKey, CaptchaOutcome.Missing);
            }

            var verifyCaptchaResponse = await _captchaEvaluator.VerifyAsync(captchaCode);
            var verified = (bool)verifyCaptchaResponse.GetType().GetProperty("Verified")!.GetValue(verifyCaptchaResponse)!;

            if (verified)
            {
                return OidcCaptchaEvaluation.Pass();
            }

            return OidcCaptchaEvaluation.Require(OAuthError.CaptchaInvalid, "Captcha answer is invalid. Please try again.", captchaConfiguration.CaptchaKey, CaptchaOutcome.Invalid);
        }

        public static IActionResult BuildResult(OidcCaptchaEvaluation evaluation)
        {
            return new BadRequestObjectResult(new
            {
                error = evaluation.Error,
                error_description = evaluation.ErrorDescription,
                captcha_required = true,
                captcha_site_key = evaluation.SiteKey
            });
        }

        public enum CaptchaOutcome
        {
            Pass,
            Missing,
            Invalid
        }

        public sealed class OidcCaptchaEvaluation
        {
            public bool Required { get; init; }
            public string? Error { get; init; }
            public string? ErrorDescription { get; init; }
            public string? SiteKey { get; init; }
            public CaptchaOutcome Outcome { get; init; } = CaptchaOutcome.Pass;

            public static OidcCaptchaEvaluation Pass() => new() { Required = false, Outcome = CaptchaOutcome.Pass };

            public static OidcCaptchaEvaluation Require(string error, string description, string? siteKey, CaptchaOutcome outcome) =>
                new() { Required = true, Error = error, ErrorDescription = description, SiteKey = siteKey, Outcome = outcome };
        }
    }
}
