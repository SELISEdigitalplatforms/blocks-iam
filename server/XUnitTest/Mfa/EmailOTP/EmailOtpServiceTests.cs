using Blocks.Genesis;
using Blocks.MailDriver;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;

namespace XUnitTest.Mfa.EmailOTP
{
    public class EmailOtpServiceTests
    {
        private static EmailOtpService CreateService(
            out Mock<ICacheClient> cache,
            out Mock<IMfaConfigurationService> config,
            out Mock<IMessageClient> message)
        {
            cache = new Mock<ICacheClient>();
            config = new Mock<IMfaConfigurationService>();
            message = new Mock<IMessageClient>();
            return new EmailOtpService(cache.Object, config.Object, message.Object);
        }

        [Fact]
        public async Task GenerateAsync_AddsMfaIdToCache_With300SecondTtl()
        {
            var service = CreateService(out var cache, out _, out _);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com" };

            string? capturedKey = null;
            long? capturedTtl = null;
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((k, _, t) => { capturedKey = k; capturedTtl = t; })
                .ReturnsAsync(true);

            var result = await service.GenerateAsync(user);

            result.IsSuccess.Should().BeTrue();
            capturedKey.Should().NotBeNullOrEmpty();
            capturedTtl.Should().Be(300);
        }

        [Fact]
        public async Task GenerateAsync_WithoutPhoneDomain_SendsToUserEmail()
        {
            var service = CreateService(out var cache, out _, out var message);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            await service.GenerateAsync(user);

            captured.Should().NotBeNull();
            captured!.Payload.To.Should().ContainSingle().Which.Should().Be("u1@e.com");
            captured.ConsumerName.Should().Be(IdpConstants.MailQueue);
        }

        [Fact]
        public async Task GenerateAsync_WithPhoneDomain_AndMissingPhoneNumber_ReturnsError()
        {
            var service = CreateService(out var cache, out _, out _);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com", PhoneNumber = "" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);

            var result = await service.GenerateAsync(user, "sms.example.com");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("phonenumber_not_exist");
        }

        [Fact]
        public async Task GenerateAsync_WithPhoneDomain_BuildsPhoneAsEmailAddress()
        {
            var service = CreateService(out var cache, out _, out var message);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com", PhoneNumber = "+1 (555) 123-4567" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            await service.GenerateAsync(user, "sms.example.com");

            captured!.Payload.To.Should().ContainSingle().Which.Should().Be("001(555)123-4567@sms.example.com");
        }

        [Fact]
        public async Task VerifyAsync_WhenKeyMissing_ReturnsInvalidTwoFactorIdError()
        {
            var service = CreateService(out var cache, out _, out _);
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "missing" });

            result.Errors.Should().ContainKey("message");
            result.Errors["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task ResendAsync_WhenKeyMissing_ReturnsInvalidTwoFactorId()
        {
            var service = CreateService(out var cache, out _, out _);
            cache.Setup(c => c.KeyExistsAsync("gone")).ReturnsAsync(false);

            var result = await service.ResendAsync("gone", new UserInfo { ItemId = "u1", Email = "u1@e.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task ResendAsync_AfterCooldown_PreservesMfaId_RegeneratesCode_AndResends()
        {
            var service = CreateService(out var cache, out _, out var message);
            // Original challenge sent more than the cooldown ago.
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            var originalCode = context.MfaCode;
            context.LastSentUtc = DateTime.UtcNow.AddSeconds(-120);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());

            string? storedKey = null;
            string? storedValue = null;
            long? storedTtl = null;
            cache.Setup(c => c.AddStringValueAsync("m1", It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((k, v, t) => { storedKey = k; storedValue = v; storedTtl = t; })
                .ReturnsAsync(true);
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            var result = await service.ResendAsync("m1", new UserInfo { ItemId = "u1", Email = "u1@e.com", Language = "en-US" });

            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().Be("m1");                     // same id preserved
            storedKey.Should().Be("m1");
            storedTtl.Should().Be(300);                         // TTL reset
            var updated = MfaAuthenticationContext.Deserialize(storedValue!);
            updated.MfaCode.Should().NotBe(originalCode);        // new code minted
            captured.Should().NotBeNull();
            captured!.Payload.To.Should().ContainSingle().Which.Should().Be("u1@e.com");
        }

        [Fact]
        public async Task ResendAsync_WithinCooldown_ReturnsTooSoon_WithRetryAfter_AndDoesNotResend()
        {
            var service = CreateService(out var cache, out _, out var message);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            context.LastSentUtc = DateTime.UtcNow.AddSeconds(-5);   // still inside the 60s window
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());

            var result = await service.ResendAsync("m1", new UserInfo { ItemId = "u1", Email = "u1@e.com" });

            result.IsSuccess.Should().BeFalse();
            result.MfaId.Should().Be("m1");
            result.Errors["message"].Should().Be("resend_too_soon");
            result.Errors.Should().ContainKey("retry_after_seconds");
            cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_WhenCodeMatches_RemovesKeyAndReturnsValid()
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());
            cache.Setup(c => c.RemoveKeyAsync("m1")).ReturnsAsync(true);

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = context.MfaCode });

            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be("u1");
            cache.Verify(c => c.RemoveKeyAsync("m1"), Times.Once);
        }

        [Fact]
        public async Task VerifyAsync_WhenCodeMismatch_ReturnsInvalidCodeError()
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = "99999" });

            result.IsValid.Should().BeFalse();
            result.Errors["message"].Should().Be("invalid_two_factor_code");
        }

        [Fact]
        public async Task SendMfaCodeAsync_UsesConfiguredTemplateName_WhenProvided()
        {
            var service = CreateService(out var cache, out var config, out var message);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com", Language = "en-US" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new global::Mfa.DomainService.Configuration.Configuration { MfaTemplate = new MfaTemplate { TemplateName = "CustomTemplate" } });
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            await service.GenerateAsync(user);

            captured!.Payload.Purpose.Should().Be("CustomTemplate");
        }

        [Fact]
        public async Task SendMfaCodeAsync_FallsBackToDefaultTemplate_WhenConfigTemplateMissing()
        {
            var service = CreateService(out var cache, out var config, out var message);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            config.Setup(c => c.GetAsync()).ReturnsAsync(new global::Mfa.DomainService.Configuration.Configuration());
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            await service.GenerateAsync(user);

            captured!.Payload.Purpose.Should().Be("MfaViaEmail");
        }
    }
}
