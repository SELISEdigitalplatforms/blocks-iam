using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared;
using Blocks.CaptchaDriver;
using FluentAssertions;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Auth
{
    public class OidcCaptchaEvaluatorTests
    {
        private static OidcCaptchaEvaluator CreateEvaluator(out Mock<ICaptchaEvaluator> captcha)
        {
            captcha = new Mock<ICaptchaEvaluator>();
            return new OidcCaptchaEvaluator(captcha.Object);
        }

        [Fact]
        public async Task EvaluateAsync_Passes_WhenCaptchaNotRequired()
        {
            var evaluator = CreateEvaluator(out _);
            var user = new User { FailedLoginCount = 0 };

            var result = await evaluator.EvaluateAsync(user, "any-code");

            result.Required.Should().BeFalse();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Pass);
        }

        [Fact]
        public async Task EvaluateAsync_Passes_WhenConfigMissing()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync()).ReturnsAsync((CaptchaConfiguration?)null);

            var result = await evaluator.EvaluateAsync(user, "any-code");

            result.Required.Should().BeFalse();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Pass);
        }

        [Fact]
        public async Task EvaluateAsync_Passes_WhenCaptchaDisabled()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = false });

            var result = await evaluator.EvaluateAsync(user, "any-code");

            result.Required.Should().BeFalse();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Pass);
        }

        [Fact]
        public async Task EvaluateAsync_RequiresMissingCaptchaCode()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });

            var result = await evaluator.EvaluateAsync(user, "");

            result.Required.Should().BeTrue();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Missing);
            result.SiteKey.Should().Be("site-key");
            result.Error.Should().Be(OAuthError.CaptchaEnabled);
        }

        [Fact]
        public async Task EvaluateAsync_RequiresMissingCaptchaCode_WhenWhitespace()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });

            var result = await evaluator.EvaluateAsync(user, "   ");

            result.Required.Should().BeTrue();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Missing);
        }

        [Fact]
        public async Task EvaluateAsync_Passes_WhenCaptchaVerified()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });
            captcha.Setup(c => c.VerifyAsync("good-code"))
                .ReturnsAsync(new { Verified = true });

            var result = await evaluator.EvaluateAsync(user, "good-code");

            result.Required.Should().BeFalse();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Pass);
        }

        [Fact]
        public async Task EvaluateAsync_RequiresInvalidCaptcha()
        {
            var evaluator = CreateEvaluator(out var captcha);
            var user = new User { FailedLoginCount = 10 };
            captcha.Setup(c => c.GetConfigurationAsync())
                .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key" });
            captcha.Setup(c => c.VerifyAsync("bad-code"))
                .ReturnsAsync(new { Verified = false });

            var result = await evaluator.EvaluateAsync(user, "bad-code");

            result.Required.Should().BeTrue();
            result.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Invalid);
            result.Error.Should().Be(OAuthError.CaptchaInvalid);
            result.SiteKey.Should().Be("site-key");
        }

        [Fact]
        public void BuildResult_ReturnsBadRequestWithCaptchaInfo()
        {
            var evaluation = OidcCaptchaEvaluator.OidcCaptchaEvaluation.Require(
                OAuthError.CaptchaInvalid, "Invalid", "site-key", OidcCaptchaEvaluator.CaptchaOutcome.Invalid);

            var result = OidcCaptchaEvaluator.BuildResult(evaluation);

            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = (BadRequestObjectResult)result;
            badRequest.StatusCode.Should().Be(400);
        }

        [Fact]
        public void Pass_FactoryCreatesPassEvaluation()
        {
            var evaluation = OidcCaptchaEvaluator.OidcCaptchaEvaluation.Pass();
            evaluation.Required.Should().BeFalse();
            evaluation.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Pass);
            evaluation.Error.Should().BeNull();
            evaluation.SiteKey.Should().BeNull();
        }

        [Fact]
        public void Require_FactoryCreatesRequireEvaluation()
        {
            var evaluation = OidcCaptchaEvaluator.OidcCaptchaEvaluation.Require(
                "error-code", "error-desc", "site-key", OidcCaptchaEvaluator.CaptchaOutcome.Missing);

            evaluation.Required.Should().BeTrue();
            evaluation.Outcome.Should().Be(OidcCaptchaEvaluator.CaptchaOutcome.Missing);
            evaluation.Error.Should().Be("error-code");
            evaluation.ErrorDescription.Should().Be("error-desc");
            evaluation.SiteKey.Should().Be("site-key");
        }
    }
}