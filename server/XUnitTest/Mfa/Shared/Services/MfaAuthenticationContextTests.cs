using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;

namespace XUnitTest.Mfa.Shared.Services
{
    public class MfaAuthenticationContextTests
    {
        [Fact]
        public void Create_PopulatesUserIdAndMfaId_AndGeneratesMfaCode()
        {
            var context = MfaAuthenticationContext.Create("mfa-123", "user-1", UserMfaType.Email);

            context.MfaId.Should().Be("mfa-123");
            context.UserId.Should().Be("user-1");
            context.MfaCode.Should().NotBeNullOrEmpty();
            context.MfaType.Should().Be(UserMfaType.Email);
            context.LastSentUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Sterilize_And_Deserialize_RoundTripPreservesAllFields()
        {
            var original = MfaAuthenticationContext.Create("mfa-1", "user-1", UserMfaType.Email);
            var json = original.Sterilize();

            var deserialized = MfaAuthenticationContext.Deserialize(json);

            deserialized.MfaId.Should().Be(original.MfaId);
            deserialized.UserId.Should().Be(original.UserId);
            deserialized.MfaCode.Should().Be(original.MfaCode);
            deserialized.MfaType.Should().Be(original.MfaType);
        }

        [Fact]
        public void TryDeserialize_OnContextJson_ReturnsTrue()
        {
            var json = MfaAuthenticationContext.Create("mfa-1", "user-1", UserMfaType.Email).Sterilize();

            MfaAuthenticationContext.TryDeserialize(json, out var ctx).Should().BeTrue();
            ctx.UserId.Should().Be("user-1");
        }

        [Fact]
        public void TryDeserialize_OnBareUserIdString_ReturnsFalse()
        {
            // A TOTP session stores a bare user id, not a context — must not be treated as resendable.
            MfaAuthenticationContext.TryDeserialize("some-user-id", out _).Should().BeFalse();
        }

        [Fact]
        public void GenerateSecureRandomNumber_Returns5DigitString()
        {
            var code = MfaAuthenticationContext.GenerateSecureRandomNumber();

            code.Should().NotBeNullOrEmpty();
            code.Length.Should().Be(5);
            int.TryParse(code, out _).Should().BeTrue();
        }

        [Fact]
        public void GenerateSecureRandomNumber_GeneratesInExpectedRange()
        {
            for (var i = 0; i < 100; i++)
            {
                var value = int.Parse(MfaAuthenticationContext.GenerateSecureRandomNumber());
                value.Should().BeInRange(11111, 99999);
            }
        }

        [Fact]
        public void Deserialize_OnInvalidJson_Throws()
        {
            Action act = () => MfaAuthenticationContext.Deserialize("not-json");
            act.Should().Throw<Exception>();
        }
    }
}
