using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Actions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Moq;
using System.Text.Json;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Mfa.Shared.Services
{
    /// <summary>The tenant hears about an MFA change only after it was saved, and only as ids and flags.</summary>
    public sealed class MfaManagementServiceActionTests
    {
        private readonly Mock<IOtpServiceFactory> _factory = new();
        private readonly Mock<IMfaManagementRepository> _repo = new();
        private readonly Mock<IActionPublisher> _actions = new();
        private readonly List<(string Action, string Json)> _published = new();

        public MfaManagementServiceActionTests()
        {
            _actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) => _published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.UpdatePartialAsync<UserMfaInfo>(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }

        private MfaManagementService Create() => new(
            _factory.Object, _repo.Object, new Mock<IMfaConfigurationService>().Object, new Mock<ICacheClient>().Object,
            new Mock<ITotpSecretResolver>().Object, actionPublisher: _actions.Object);

        private void OtpResult(bool isValid)
        {
            var otp = new Mock<IOtpService>();
            otp.Setup(s => s.VerifyAsync(It.IsAny<VerifyOtpRequest>())).ReturnsAsync(new OtpVerificationResponse { IsValid = isValid, UserId = "u1" });
            _factory.Setup(f => f.GetOTPService(It.IsAny<UserMfaType>())).Returns(otp.Object);
        }

        [Fact]
        public async Task EnablingMfaWithAValidCode_PublishesEnabledBySelf()
        {
            OtpResult(true);

            await Create().VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.TOTP, IsFromTokenCall = false });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserMfaChanged, "{\"userId\":\"u1\",\"enabled\":true,\"by\":\"self\"}"));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task ALoginCodeOrAWrongCode_ChangesNothing_AndPublishesNothing(bool valid, bool fromTokenCall)
        {
            OtpResult(valid);

            await Create().VerifyOTPAsync(new VerifyOtpRequest { AuthType = UserMfaType.TOTP, IsFromTokenCall = fromTokenCall });

            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task AdminReset_PublishesDisabledByAdmin()
        {
            await Create().DisableUserMfa(new DisableUserMfaRequest { UserId = "target", AdminActorUserId = "admin-1", Reason = "lost device" });

            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.UserMfaChanged, "{\"userId\":\"target\",\"enabled\":false,\"by\":\"admin\"}"));
            _published[0].Json.Should().NotContain("lost device").And.NotContain("admin-1");
        }

        [Fact]
        public async Task ARefusedDisable_PublishesNothing()
        {
            var result = await Create().DisableUserMfa(new DisableUserMfaRequest { UserId = "target" });

            result.IsSuccess.Should().BeFalse();
            _published.Should().BeEmpty();
        }
    }
}
