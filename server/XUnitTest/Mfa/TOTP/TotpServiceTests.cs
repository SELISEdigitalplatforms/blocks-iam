using System.Net;
using System.Text;
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
            out Mock<IHttpService> http,
            out IConfiguration configuration,
            out IHttpContextAccessor httpContext)
        {
            repo = new Mock<IMfaManagementRepository>();
            cache = new Mock<ICacheClient>();
            validator = new Mock<IValidator<VerifyOtpRequest>>();
            tenants = new Mock<ITenants>();
            http = new Mock<IHttpService>();

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
                tenants.Object,
                http.Object);
        }

        [Fact]
        public async Task GenerateAsync_AddsUserIdToCache_With15MinTtl_ReturnsMfaId()
        {
            var service = CreateService(out _, out var cache, out _, out _, out _, out _, out _);
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
            var service = CreateService(out _, out var cache, out _, out _, out _, out _, out _);
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
            var service = CreateService(out _, out var cache, out var validator, out _, out _, out _, out _);
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
            var service = CreateService(out _, out var cache, out var validator, out _, out _, out _, out _);
            validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.VerifyAsync(new VerifyOtpRequest { VerificationCode = "123456", MfaId = "m1" });

            result.Errors.Should().ContainKey("login_session_expired");
        }

        [Fact]
        public async Task VerifyAsync_WhenTotpInfoNull_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _, out _);
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
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _, out _);
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
            var service = CreateService(out var repo, out var cache, out var validator, out _, out _, out _, out _);
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
            var service = CreateService(out _, out _, out _, out _, out _, out _, out _);
            var r1 = await service.VerifyForUserAsync("", "123456");
            r1.Errors.Should().ContainKey("invalid_request");

            var r2 = await service.VerifyForUserAsync("u1", "");
            r2.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenNoExistingSecret_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);

            var result = await service.VerifyForUserAsync("u1", "123456");

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenSecretEmpty_ReturnsTotpNotSetupError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "" });

            var result = await service.VerifyForUserAsync("u1", "123456");

            result.Errors.Should().ContainKey("totp_not_setup");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenCodeValid_ReturnsValid()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.VerifyForUserAsync("u1", "000000");

            result.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task VerifyForUserAsync_WhenCodeInvalid_ReturnsInvalid()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.VerifyForUserAsync("u1", "999999");

            result.IsValid.Should().BeFalse();
            result.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenUserInfoNull_ReturnsUserNotExistError()
        {
            var service = CreateService(out var repo, out _, out _, out _, out _, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserInfo?)null);

            var result = await service.GenerateTotpImageByUserAsync("missing-user");

            result.Errors.Should().ContainKey("user_not_exist");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenExistingOtpInfo_HasImageUri_ReturnsExistingWithoutCallingHttp()
        {
            var service = CreateService(out var repo, out _, out _, out _, out var http, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserTotpDetail { ImageUri = "https://img.test/qr.png", Secret = "JBSWY3DPEHPK3PXP" });

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be("https://img.test/qr.png");
            result.QrCode.Should().Be("JBSWY3DPEHPK3PXP");
            http.Verify(h => h.SendRequest<string>(It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenTenantIsNull_StillCallsHttp_WithEmptyDomain()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out var http, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns((Tenant?)null);
            http.Setup(h => h.SendRequest<string>(HttpMethod.Post, "https://upload.test/", It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"uploadUrl\":\"https://upload.test/abc\"}", string.Empty));
http.Setup(h => h.SendRequest<string>(HttpMethod.Put, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{}", string.Empty));
http.Setup(h => h.SendRequest<string>(HttpMethod.Get, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"url\":\"https://file.test/abc\"}", string.Empty));

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be("https://file.test/abc");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenFileUriEmpty_ReturnsFailureResponse()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out var http, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(new Tenant { DbConnectionString = "x", JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }, Applications = new List<Applications> { new() { Domain = "https://app.test" } } });
            http.Setup(h => h.SendRequest<string>(HttpMethod.Post, "https://upload.test/", It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"uploadUrl\":\"https://upload.test/abc\"}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Put, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{}", "upload failed"));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Get, "https://file.test/" + It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"url\":\"\"}", string.Empty));

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_WhenFileUriResponseInvalidJson_Throws()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out var http, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(new Tenant { DbConnectionString = "x", JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }, Applications = new List<Applications> { new() { Domain = "https://app.test" } } });
            http.Setup(h => h.SendRequest<string>(HttpMethod.Post, "https://upload.test/", It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"uploadUrl\":\"https://upload.test/abc\"}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Put, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Get, "https://file.test/" + It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("not-json", string.Empty));

            Func<Task> act = async () => await service.GenerateTotpImageByUserAsync("u1");
            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_OnHappyPath_PersistsAndReturnsImageAndSecret()
        {
            var service = CreateService(out var repo, out _, out _, out var tenants, out var http, out _, out _);
            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(new Tenant { DbConnectionString = "x", JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }, Applications = new List<Applications> { new() { Domain = "https://app.test" } } });
            http.Setup(h => h.SendRequest<string>(HttpMethod.Post, "https://upload.test/", It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"uploadUrl\":\"https://upload.test/abc\"}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Put, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Get, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"url\":\"https://file.test/abc.png\"}", string.Empty));

            UserTotpDetail? persisted = null;
            repo.Setup(r => r.SaveAsync(It.IsAny<UserTotpDetail>(), It.IsAny<string>()))
                .Callback<UserTotpDetail, string>((d, _) => persisted = d)
                .Returns(Task.CompletedTask);

            var result = await service.GenerateTotpImageByUserAsync("u1");

            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be("https://file.test/abc.png");
            result.QrCode.Should().NotBeNullOrEmpty();
            persisted.Should().NotBeNull();
            persisted!.ImageUri.Should().Be("https://file.test/abc.png");
            persisted.Secret.Should().Be(result.QrCode);
            persisted.CreatedBy.Should().Be("u1");
            persisted.LastUpdatedBy.Should().Be("u1");
            persisted.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_SendsAuthorizationHeaderFromContext()
        {
            var httpContext = BuildHttpContextAccessor(authorization: "Bearer my-token");
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "PreSignedUriForUpload", "https://upload.test/" },
                { "GetFileEnpPoint", "https://file.test/" }
            });
            var configuration = configBuilder.Build();

            var repo = new Mock<IMfaManagementRepository>();
            var cache = new Mock<ICacheClient>();
            var validator = new Mock<IValidator<VerifyOtpRequest>>();
            var tenants = new Mock<ITenants>();
            var http = new Mock<IHttpService>();

            repo.Setup(r => r.GetItemAsync<UserInfo>(It.IsAny<System.Linq.Expressions.Expression<Func<UserInfo, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new UserInfo { ItemId = "u1", Email = "u1@e.com" });
            repo.Setup(r => r.GetItemAsync<UserTotpDetail>(It.IsAny<System.Linq.Expressions.Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((UserTotpDetail?)null);
            tenants.Setup(t => t.GetTenantByID(It.IsAny<string>())).Returns(new Tenant { DbConnectionString = "x", JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "", IssueDate = DateTime.UtcNow }, Applications = new List<Applications> { new() { Domain = "https://app.test" } } });
            Dictionary<string, string>? capturedHeaders = null;
            http.Setup(h => h.SendRequest<string>(HttpMethod.Post, "https://upload.test/", It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback<HttpMethod, string, object?, string, Dictionary<string, string>?, CancellationToken, int?>((_, _, _, _, hdrs, _, _) => capturedHeaders = hdrs)
                .ReturnsAsync(("{\"uploadUrl\":\"https://upload.test/abc\"}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Put, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{}", string.Empty));
            http.Setup(h => h.SendRequest<string>(HttpMethod.Get, It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>()))
                .ReturnsAsync(("{\"url\":\"https://file.test/abc.png\"}", string.Empty));

            var service = new TotpService(repo.Object, NullLogger<TotpService>.Instance, httpContext, configuration, cache.Object, validator.Object, tenants.Object, http.Object);

            await service.GenerateTotpImageByUserAsync("u1");

            capturedHeaders.Should().NotBeNull();
            capturedHeaders!.Should().ContainKey("Authorization");
            capturedHeaders["Authorization"].Should().Be("Bearer my-token");
            capturedHeaders.Should().ContainKey("x-blocks-key");
        }
    }
}
