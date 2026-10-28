using System.Text.Json;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Mfa.TOTP
{
    public class TotpServiceTests
    {
        private static IHttpContextAccessor BuildHttpContextAccessor(string? authorization = "Bearer test-token", string? xBlocksKey = "key-1")
        {
            var ctx = new DefaultHttpContext();
            if (authorization != null)
            {
                ctx.Request.Headers["Authorization"] = authorization;
            }
            if (xBlocksKey != null)
            {
                ctx.Request.Headers["x-blocks-key"] = xBlocksKey;
            }
            return new HttpContextAccessor { HttpContext = ctx };
        }

        private static TotpService CreateService(
            out Mock<IMfaManagementRepository> repo,
            out Mock<ICacheClient> cache,
            out Mock<IValidator<VerifyOtpRequest>> validator,
            out Mock<ITenants> tenants,
            out IConfiguration configuration,
            out IHttpContextAccessor httpContext)
        {
            repo = new Mock<IMfaManagementRepository>();
            cache = new Mock<ICacheClient>();
            validator = new Mock<IValidator<VerifyOtpRequest>>();
            tenants = new Mock<ITenants>();

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "PreSignedUriForUpload", "https://upload.test/" },
                { "GetFileEnpPoint", "https://file.test/" }
            });
            configuration = configBuilder.Build();
            httpContext = BuildHttpContextAccessor();

            return new TotpService(
                repo.Object,
                NullLogger<TotpService>.Instance,
                httpContext,
                configuration,
                cache.Object,
                validator.Object,
                tenants.Object);
        }

        [Fact]
        public async Task GenerateAsync_AddsUserIdToCache_With15MinTtl_ReturnsMfaId()
        {
            var service = CreateService(out _, out var cache, out _, out _, out _, out _);
            var user = new UserInfo { ItemId = "u1" };

            string? capturedKey = null;
            string? capturedValue = null;
            long? capturedTtl = null;
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((k, v, t) => { capturedKey = k; capturedValue = v; capturedTtl = t; })
                .ReturnsAsync(true);

            var result = await service.GenerateAsync(user);

            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().NotBeNullOrEmpty();
            capturedKey.Should().Be(result.MfaId);
            capturedValue.Should().Be("u1");
            capturedTtl.Should().Be(15 * 60);
        }

        [Fact]
        public async Task GenerateAsync_WithNullUserItemId_StoresEmptyString()
        {
            var service = CreateService(out _, out var cache, out _, out _, out _, out _);
            string? capturedValue = null;
            cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                .Callback<string, string, long>((_, v, _) => capturedValue = v)
                .ReturnsAsync(true);

            await service.GenerateAsync(new UserInfo());

            capturedValue.Should().Be(string.Empty);
        }

        [Fact]
        public async Task VerifyAsync_WhenValidatorFails_ReturnsValidationErrors()
        {
            var service = CreateService(out _, out var cache, out var validator, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new List<ValidationFailure>
                {
                    new("VerificationCode", "required")
                }));

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "", MfaId = "" });

            result.Errors.Should().ContainKey("VerificationCode");
            cache.Verify(c => c.KeyExistsAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_WhenCacheKeyMissing_ReturnsLoginSessionExpiredError()
        {
            var service = CreateService(out _, out var cache, out var validator, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "123456", MfaId = "m1" });

            result.Errors.Should().ContainKey("login_session_expired");
        }

        [Fact]
        public async Task VerifyAsync_WhenTotpInfoNull_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("u1");
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "123456", MfaId = "m1" });

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyAsync_WhenTotpInfoSecretEmpty_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("u1");
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "" });

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "123456", MfaId = "m1" });

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyAsync_WhenTotpCodeInvalid_ReturnsInvalid()
        {
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync("u1");
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "000000", MfaId = "m1" });

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenUserIdOrCodeEmpty_ReturnsError()
        {
            var service = CreateService(out _, out _, out _, out _, out _, out _);
            var r1 = await service.VerifyForUserAsync("", "123456");
            r1.Errors.Should().ContainKey("invalid_request");

            var r2 = await service.VerifyForUserAsync("u1", "");
            r2.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenNoExistingSecret_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);

            var result = await service.VerifyForUserAsync("u1", "123456");

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenSecretEmpty_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "" });

            var result = await service.VerifyForUserAsync("u1", "123456");

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenCodeValid_ReturnsValid()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.VerifyForUserAsync("u1", "000000");

            result.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenCodeInvalid_ReturnsInvalid()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.VerifyForUserAsync("u1", "999999");

            result.IsValid.Should().BeFalse();
            result.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenUserInfoNull_ReturnsUserNotExistError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserInfo?)null);

            var result = await service.GenerateTotpImageByUserAsync("missing-user");

            result.Errors.Should().ContainKey("user_not_exist");
        }

        private static void SetupUser(Mock<IMfaManagementRepository> repo, UserInfo user, UserTotpDetail? existing)
        {
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(user);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(existing!);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenTotpIsActive_IsRefused_AndReturnsNoSecret()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _);
            SetupUser(repo, new UserInfo { ItemId = "u1", MfaEnabled = true, UserMfaType = UserMfaType.TOTP },
                new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP", CreatedBy = "u1" });

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.Errors.Should().ContainKey("totp_already_enabled");
            result.QrCode.Should().BeNullOrEmpty();
            result.QrImageUrl.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_NewSetup_ReturnsInlineQr_AndStoresNoImageFile()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out _, out _);
            SetupUser(repo, new UserInfo { ItemId = "u1", Email = "u1@e.com" }, null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            UserTotpDetail? saved = null;
            repo.Setup(r => r.SaveAsync(It.IsAny<UserTotpDetail>(), It.IsAny<string>()))
                .Callback<UserTotpDetail, string>((d, _) => saved = d).Returns(Task.CompletedTask);

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().StartWith("data:image/png;base64,");
            saved.Should().NotBeNull();
            saved!.ImageUri.Should().BeEmpty();
            saved.Secret.Should().Be(result.QrCode);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_OldSecretWithPublicQr_IsDeleted_AndReplaced()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out _, out _);
            SetupUser(repo, new UserInfo { ItemId = "u1", Email = "u1@e.com" },
                new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP", ImageUri = "https://img.test/qr.png", CreatedBy = "u1", CreatedDate = DateTime.UtcNow });
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.QrCode.Should().NotBe("JBSWY3DPEHPK3PXP");
            repo.Verify(r => r.DeleteItemsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>()), Times.Once);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_UnconfirmedSetupFromAMinuteAgo_KeepsTheSameSecret()
        {
            // A page reload must not invalidate the QR the user may already have scanned.
            var service = CreateService(out var repo, out _, out _, out var tenants, out _, out _);
            SetupUser(repo, new UserInfo { ItemId = "u1", Email = "u1@e.com" },
                new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP", ImageUri = string.Empty, CreatedBy = "u1", CreatedDate = DateTime.UtcNow.AddMinutes(-1) });
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.QrCode.Should().Be("JBSWY3DPEHPK3PXP");
            repo.Verify(r => r.SaveAsync(It.IsAny<UserTotpDetail>(), It.IsAny<string>()), Times.Never);
        }

        // ---------- single use: mfa_id consumption and time-step replay ----------

        private const string TotpSecret = "JBSWY3DPEHPK3PXP";

        /// <summary>A TotpService over stateful fakes: mfa ids live in a dictionary whose removal
        /// reports whether THIS call removed it, and the used-step marker behaves like SET NX.</summary>
        private static TotpService CreateStatefulService(out System.Collections.Concurrent.ConcurrentDictionary<string, string> sessions)
        {
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _);
            var store = sessions = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var usedSteps = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync((string k) => store.ContainsKey(k));
            cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>())).ReturnsAsync((string k) => store.TryGetValue(k, out var v) ? v : null!);
            cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).ReturnsAsync((string k) => store.TryRemove(k, out _));
            var db = new Mock<StackExchange.Redis.IDatabase>();
            db.Setup(d => d.StringSetAsync(It.IsAny<StackExchange.Redis.RedisKey>(), It.IsAny<StackExchange.Redis.RedisValue>(), It.IsAny<TimeSpan?>(), StackExchange.Redis.When.NotExists))
                .ReturnsAsync((StackExchange.Redis.RedisKey k, StackExchange.Redis.RedisValue _, TimeSpan? _, StackExchange.Redis.When _) => usedSteps.TryAdd(k.ToString(), true));
            cache.Setup(c => c.CacheDatabase()).Returns(db.Object);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = TotpSecret, CreatedBy = "u1" });
            return service;
        }

        private static string CodeAt(DateTime utc) =>
            new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(TotpSecret)).ComputeTotp(utc);

        [Fact]
        public async Task VerifyAsync_SameMfaIdAndCodeTwice_SecondIsRefused()
        {
            var service = CreateStatefulService(out var sessions);
            sessions["m1"] = "u1";
            var code = CodeAt(DateTime.UtcNow);

            var first = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" });
            var second = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" });

            first.IsValid.Should().BeTrue();
            sessions.Should().NotContainKey("m1");
            second.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_SameCodeUnderANewMfaId_InTheSameTimeStep_IsRefused()
        {
            var service = CreateStatefulService(out var sessions);
            sessions["m1"] = "u1";
            sessions["m2"] = "u1";
            var code = CodeAt(DateTime.UtcNow);

            (await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" })).IsValid.Should().BeTrue();
            var replay = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m2" });

            replay.IsValid.Should().BeFalse();
            replay.UserId.Should().Be("u1");   // returned so the replay is counted as a failure
        }

        [Fact]
        public async Task VerifyAsync_ADifferentTimeStep_StillWorks()
        {
            var service = CreateStatefulService(out var sessions);
            sessions["m1"] = "u1";
            sessions["m2"] = "u1";
            // Previous step is inside the accepted drift window; it is a different step from "now".
            var previous = CodeAt(DateTime.UtcNow.AddSeconds(-30));
            var current = CodeAt(DateTime.UtcNow);
            previous.Should().NotBe(current);

            (await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = current, MfaId = "m1" })).IsValid.Should().BeTrue();
            (await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = previous, MfaId = "m2" })).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task VerifyAsync_ParallelDoubleSubmit_OnlyOneSucceeds()
        {
            for (var round = 0; round < 25; round++)
            {
                var service = CreateStatefulService(out var sessions);
                sessions["m1"] = "u1";
                var code = CodeAt(DateTime.UtcNow);

                var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                    Task.Run(() => service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m1" }))));

                results.Count(r => r.IsValid).Should().Be(1);
            }
        }

        [Fact]
        public async Task VerifyAsync_ParallelSameCodeUnderDifferentMfaIds_OnlyOneSucceeds()
        {
            var service = CreateStatefulService(out var sessions);
            for (var i = 0; i < 8; i++) sessions["m" + i] = "u1";
            var code = CodeAt(DateTime.UtcNow);

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                Task.Run(() => service.VerifyAsync(new VerifyOtpRequest { VerificationCode = code, MfaId = "m" + i }))));

            results.Count(r => r.IsValid).Should().Be(1);
        }

        [Fact]
        public async Task VerifyAsync_WrongCode_DoesNotConsumeTheSession()
        {
            var service = CreateStatefulService(out var sessions);
            sessions["m1"] = "u1";
            var wrong = CodeAt(DateTime.UtcNow) == "000000" ? "000001" : "000000";

            (await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = wrong, MfaId = "m1" })).IsValid.Should().BeFalse();

            sessions.Should().ContainKey("m1");
            (await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = CodeAt(DateTime.UtcNow), MfaId = "m1" })).IsValid.Should().BeTrue();
        }
    }
}
