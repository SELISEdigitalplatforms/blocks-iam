using Blocks.Genesis;
using Iam.DomainService.Dtos;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;
using Blocks.Genesis.Entities.Iam;

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
        public async Task GenerateAsync_QueuePayloadJson_HasSendPhoneNumberAsEmail_AndNoProjectKey()
        {
            var service = CreateService(out var cache, out _, out var message);
            var user = new UserInfo { ItemId = "u1", Email = "u1@e.com" };
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync(true);
            ConsumerMessage<SendMail>? captured = null;
            message.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
                .Callback<ConsumerMessage<SendMail>>(c => captured = c)
                .Returns(Task.CompletedTask);

            await service.GenerateAsync(user);

            captured!.Payload.Should().BeOfType<Iam.DomainService.Dtos.SendMail>();
            var json = System.Text.Json.JsonSerializer.Serialize(captured.Payload);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            names.Should().Contain("SendPhoneNumberAsEmail");
            names.Should().NotContain("ProjectKey");
            // Same fields as the logic consumer's BaseMailRequest reads.
            names.Should().Contain(new[] { "SubjectDataContext", "To", "Bcc", "Cc", "Purpose", "Language", "ReplyTo", "Attachments", "BodyDataContext" });
            doc.RootElement.GetProperty("SendPhoneNumberAsEmail").GetBoolean().Should().BeFalse();
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
            storedTtl.Should().BeInRange(1, 180);               // original expiry kept (about 180s left), not reset to 300
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

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = context.MfaCode == "000000" ? "000001" : "000000" });

            result.IsValid.Should().BeFalse();
            result.UserId.Should().Be("u1");   // owner returned so the caller can count the wrong code
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

        [Fact]
        public async Task GenerateAsync_StoresExpiryFiveMinutesAhead()
        {
            var service = CreateService(out var cache, out _, out _);
            string? stored = null;
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, v, _) => stored = v)
                .ReturnsAsync(true);

            await service.GenerateAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });

            var context = MfaAuthenticationContext.Deserialize(stored!);
            context.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(300), TimeSpan.FromSeconds(5));
            context.MfaCode.Should().MatchRegex("^[0-9]{6}$");
        }

        [Fact]
        public async Task ResendAsync_KeepsOriginalExpiry_AndKillsPreviousCode()
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            var oldCode = context.MfaCode!;
            var originalExpiry = DateTime.UtcNow.AddSeconds(100);
            context.ExpiresAtUtc = originalExpiry;
            context.LastSentUtc = DateTime.UtcNow.AddSeconds(-200);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());
            string? storedValue = null;
            long? storedTtl = null;
            cache.Setup(c => c.AddStringValueAsync("m1", It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, v, t) => { storedValue = v; storedTtl = t; })
                .ReturnsAsync(true);

            var result = await service.ResendAsync("m1", new UserInfo { ItemId = "u1", Email = "u1@e.com" });

            result.IsSuccess.Should().BeTrue();
            storedTtl.Should().BeInRange(1, 100);   // never back to 300
            var updated = MfaAuthenticationContext.Deserialize(storedValue!);
            updated.ExpiresAtUtc.Should().Be(originalExpiry);
            updated.MfaCode.Should().NotBe(oldCode);

            // The cache now holds the new context: the old code no longer verifies.
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(storedValue!);
            cache.Setup(c => c.RemoveKeyAsync("m1")).ReturnsAsync(true);
            var oldAttempt = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = oldCode });
            oldAttempt.IsValid.Should().BeFalse();
            var newAttempt = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = updated.MfaCode });
            newAttempt.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task ResendAsync_RepeatedResends_NeverMoveExpiryForward()
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            var originalExpiry = DateTime.UtcNow.AddSeconds(250);
            context.ExpiresAtUtc = originalExpiry;
            context.LastSentUtc = DateTime.UtcNow.AddSeconds(-61);
            var current = context.Sterilize();
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(() => current);
            cache.Setup(c => c.AddStringValueAsync("m1", It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, v, _) =>
                {
                    // Pretend the 60s cooldown has passed again before the next resend.
                    var c2 = MfaAuthenticationContext.Deserialize(v);
                    c2.LastSentUtc = DateTime.UtcNow.AddSeconds(-61);
                    current = c2.Sterilize();
                })
                .ReturnsAsync(true);

            for (var i = 0; i < 4; i++)
            {
                (await service.ResendAsync("m1", new UserInfo { ItemId = "u1", Email = "u1@e.com" })).IsSuccess.Should().BeTrue();
            }

            MfaAuthenticationContext.Deserialize(current).ExpiresAtUtc.Should().Be(originalExpiry);
        }

        [Fact]
        public async Task ResendAsync_AfterOriginalExpiry_IsRefused_AndSendsNothing()
        {
            var service = CreateService(out var cache, out _, out var message);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            context.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            context.LastSentUtc = DateTime.UtcNow.AddSeconds(-400);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());
            cache.Setup(c => c.RemoveKeyAsync("m1")).ReturnsAsync(true);

            var result = await service.ResendAsync("m1", new UserInfo { ItemId = "u1", Email = "u1@e.com" });

            result.IsSuccess.Should().BeFalse();
            result.Errors["message"].Should().Be("invalid_two_factor_id");
            cache.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            cache.Verify(c => c.RemoveKeyAsync("m1"), Times.Once);
            message.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()), Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("      ")]
        [InlineData(" 123456")]
        [InlineData("123456 ")]
        [InlineData("12345")]       // old 5-digit style
        [InlineData("012345")]      // leading zero where the real code has none
        [InlineData("1234567")]
        public async Task VerifyAsync_BypassInputs_AreRejected_AndKeyIsKept(string? supplied)
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            context.MfaCode = "123456";
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = supplied });

            result.IsValid.Should().BeFalse();
            result.UserId.Should().Be("u1");
            cache.Verify(c => c.RemoveKeyAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_LeadingZeroCode_MatchesOnlyExactly()
        {
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            context.MfaCode = "004217";
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());
            cache.Setup(c => c.RemoveKeyAsync("m1")).ReturnsAsync(true);

            (await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = "4217" })).IsValid.Should().BeFalse();
            (await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = "004217" })).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task VerifyAsync_WhenKeyHoldsTotpSession_ReturnsInvalidId_WithoutThrowing()
        {
            var service = CreateService(out var cache, out _, out _);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync("u1");   // TOTP session: bare user id

            var result = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = "123456" });

            result.IsValid.Should().BeFalse();
            result.Errors["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task VerifyAsync_RightCode_WhoseChallengeWasAlreadyDeleted_IsRefused()
        {
            // Two parallel requests with the right code: only the one that really deletes the challenge passes.
            var service = CreateService(out var cache, out _, out _);
            var context = MfaAuthenticationContext.Create("m1", "u1", UserMfaType.Email);
            cache.Setup(c => c.KeyExistsAsync("m1")).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync("m1")).ReturnsAsync(context.Sterilize());
            cache.SetupSequence(c => c.RemoveKeyAsync("m1")).ReturnsAsync(true).ReturnsAsync(false);

            var first = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = context.MfaCode });
            var second = await service.VerifyAsync(new VerifyOtpRequest { MfaId = "m1", VerificationCode = context.MfaCode });

            first.IsValid.Should().BeTrue();
            second.IsValid.Should().BeFalse();
        }
    }
}
