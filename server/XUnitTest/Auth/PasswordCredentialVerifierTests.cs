using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BCryptNet = BCrypt.Net.BCrypt;

namespace XUnitTest.Auth;

/// <summary>
/// #593: the verifier extracted from embedded login. Login and the signup-link password step
/// both depend on it, so its lockout, CAPTCHA and accounting rules are pinned here.
/// </summary>
public class PasswordCredentialVerifierTests
{
    private const string Salt = "tenant-salt";
    private readonly Mock<ITenants> _tenants = new();
    private readonly Mock<IAuthenticationRepository> _repo = new();
    private readonly Mock<IAccountService> _account = new();
    private readonly Mock<IUserActivityDispatcher> _activity = new();
    private readonly Mock<IAuthenticationDomainService> _authDomain = new();
    private readonly Mock<ICaptchaEvaluator> _captcha = new();

    public PasswordCredentialVerifierTests()
    {
        _tenants.Setup(t => t.GetTenantByID("t1")).Returns(new Tenant
        {
            TenantId = "t1",
            TenantSalt = Salt,
            DbConnectionString = "",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }
        });
        _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(new IdentityConfiguration());
        _authDomain.Setup(a => a.GetVisitorsIpAddresses(It.IsAny<HttpContext>())).Returns(["10.0.0.1"]);
    }

    private PasswordCredentialVerifier Sut(bool withCaptcha = true) => new(
        NullLogger<PasswordCredentialVerifier>.Instance,
        _tenants.Object,
        _repo.Object,
        _account.Object,
        _activity.Object,
        _authDomain.Object,
        withCaptcha ? _captcha.Object : null);

    private static User UserWith(string password, int failed = 0) => new()
    {
        ItemId = "u1",
        Active = true,
        IsVerified = true,
        Password = BCryptNet.HashPassword($"{password}::{Salt}", 4),
        FailedLoginCount = failed
    };

    private static HttpRequest Request() => new DefaultHttpContext().Request;

    private void CaptchaEnabled(bool verified)
    {
        _captcha.Setup(c => c.GetConfigurationAsync())
            .ReturnsAsync(new CaptchaConfiguration { IsEnable = true, CaptchaKey = "site-key", Provider = "recaptcha" });
        _captcha.Setup(c => c.VerifyAsync(It.IsAny<string>(), "recaptcha"))
            .ReturnsAsync(new VerificationResult { Verified = verified });
    }

    [Fact]
    public async Task CorrectPassword_WithTheTenantSalt_Succeeds_AndResetsCounters()
    {
        var user = UserWith("Correct#1");
        user.LastFailedLoginUtc = DateTime.UtcNow.AddMinutes(-1);

        var result = await Sut().VerifyAsync(user, "Correct#1", null, Request(), "t1");

        result.Succeeded.Should().BeTrue();
        _repo.Verify(r => r.UpdatePartialAsync<User>("u1", It.Is<Dictionary<string, object>>(d =>
            (int)d[nameof(User.FailedLoginCount)] == 0 && d.ContainsKey(nameof(User.LockoutUntilUtc))), ""), Times.Once);
    }

    [Fact]
    public async Task CorrectPassword_CleanCounters_WritesNothing()
    {
        var result = await Sut().VerifyAsync(UserWith("Correct#1"), "Correct#1", null, Request(), "t1");

        result.Succeeded.Should().BeTrue();
        _repo.Verify(r => r.UpdatePartialAsync<User>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WrongPassword_Increments_AndReports401()
    {
        _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", 5, 15, It.IsAny<DateTime>()))
            .ReturnsAsync(new User { ItemId = "u1", FailedLoginCount = 1 });

        var result = await Sut().VerifyAsync(UserWith("Correct#1"), "nope", null, Request(), "t1");

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("invalid_username_password");
        result.StatusCode.Should().Be(401);
        result.AccountLockedNow.Should().BeFalse();
        _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "failed_login_invalid_password")), Times.Once);
        _account.Verify(a => a.SendAccountLockedNotificationAsync(It.IsAny<User>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task WrongPasswordThatLocks_NotifiesTheUser_AndSaysSo()
    {
        var lockedUntil = DateTime.UtcNow.AddMinutes(15);
        _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new User { ItemId = "u1", LockoutUntilUtc = lockedUntil });

        var result = await Sut().VerifyAsync(UserWith("Correct#1"), "nope", null, Request(), "t1");

        result.AccountLockedNow.Should().BeTrue();
        _account.Verify(a => a.SendAccountLockedNotificationAsync(It.IsAny<User>(), lockedUntil), Times.Once);
        _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "failed_login_and_account_locked")), Times.Once);
    }

    [Fact]
    public async Task LockNotificationFailure_DoesNotChangeTheOutcome()
    {
        _repo.Setup(r => r.IncrementFailedLoginAndApplyLockoutAsync("u1", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new User { ItemId = "u1", LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) });
        _account.Setup(a => a.SendAccountLockedNotificationAsync(It.IsAny<User>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Sut().VerifyAsync(UserWith("Correct#1"), "nope", null, Request(), "t1");

        result.Error.Should().Be("invalid_username_password");
        result.AccountLockedNow.Should().BeTrue();
    }

    [Fact]
    public async Task LockedAccount_Is423_WithoutCheckingThePassword()
    {
        var user = UserWith("Correct#1");
        user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(5);

        var result = await Sut().VerifyAsync(user, "Correct#1", null, Request(), "t1");

        result.StatusCode.Should().Be(423);
        result.Error.Should().Be("account_locked");
        _repo.Verify(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
        _activity.Verify(a => a.SendUserActivityAsync(It.Is<UserActivityEvent>(e => e.Event == "failed_login_account_locked")), Times.Once);
    }

    [Fact]
    public async Task MissingAuthenticationConfiguration_Is400()
    {
        _repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync((IdentityConfiguration)null!);

        var result = await Sut().VerifyAsync(UserWith("x"), "x", null, Request(), "t1");

        result.Error.Should().Be("auth_config_missing");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CaptchaGate_NoCode_IsCaptchaEnabled_AndThePasswordIsNotChecked()
    {
        CaptchaEnabled(verified: true);

        var result = await Sut().VerifyAsync(UserWith("Correct#1", failed: 2), "Correct#1", null, Request(), "t1");

        result.Error.Should().Be("captcha_enabled");
        result.CaptchaRequired.Should().BeTrue();
        result.CaptchaSiteKey.Should().Be("site-key");
        _repo.Verify(r => r.IncrementFailedLoginAndApplyLockoutAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task CaptchaGate_InvalidCode_IsCaptchaInvalid()
    {
        CaptchaEnabled(verified: false);

        var result = await Sut().VerifyAsync(UserWith("Correct#1", failed: 2), "Correct#1", "bad", Request(), "t1");

        result.Error.Should().Be("captcha_invalid");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CaptchaGate_ValidCode_ContinuesToThePassword()
    {
        CaptchaEnabled(verified: true);

        var result = await Sut().VerifyAsync(UserWith("Correct#1", failed: 2), "Correct#1", "good", Request(), "t1");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CaptchaGate_DisabledForTheTenant_IsSkipped()
    {
        _captcha.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new CaptchaConfiguration { IsEnable = false });

        var result = await Sut().VerifyAsync(UserWith("Correct#1", failed: 3), "Correct#1", null, Request(), "t1");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task CaptchaGate_BelowThreshold_OrWithoutEvaluator_IsSkipped()
    {
        CaptchaEnabled(verified: false);

        (await Sut().VerifyAsync(UserWith("Correct#1", failed: 1), "Correct#1", null, Request(), "t1")).Succeeded.Should().BeTrue();
        (await Sut(withCaptcha: false).VerifyAsync(UserWith("Correct#1", failed: 4), "Correct#1", null, Request(), "t1")).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("", "hash")]
    [InlineData("pw", null)]
    [InlineData("pw", "")]
    [InlineData("pw", "not-a-bcrypt-hash")]
    public void VerifyPassword_RejectsMissingOrMalformedInput(string? password, string? hash)
    {
        Sut().VerifyPassword(password, hash).Should().BeFalse();
    }

    [Fact]
    public void BuildPasswordMaterial_AppendsTheSaltOnlyWhenPresent()
    {
        PasswordCredentialVerifier.BuildPasswordMaterial("pw", "s").Should().Be("pw::s");
        PasswordCredentialVerifier.BuildPasswordMaterial("pw", null).Should().Be("pw");
    }

    [Fact]
    public async Task NoHttpContext_SkipsTimelineEvents()
    {
        var user = UserWith("Correct#1");
        user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(5);

        await Sut().VerifyAsync(user, "Correct#1", null, null, "t1");

        _activity.Verify(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>()), Times.Never);
    }
}
