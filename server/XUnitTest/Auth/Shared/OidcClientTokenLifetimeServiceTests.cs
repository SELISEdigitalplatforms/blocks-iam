using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// iam SPEC5: per-OIDC-client token lifetimes — store, overlay on the project configuration and
    /// request validation.
    /// </summary>
    public class OidcClientTokenLifetimeServiceTests
    {
        private readonly Mock<IAuthenticationRepository> _repo = new();

        private OidcClientTokenLifetimeService Sut() =>
            new(_repo.Object, NullLogger<OidcClientTokenLifetimeService>.Instance);

        private static IdentityConfiguration Project() => new()
        {
            AllowedGrantTypes = ["authorization_code", "refresh_token"],
            RefreshTokenValidForNumberMinutes = 30,
            AbsoluteRefreshTokenValidForNumberMinutes = 10080,
            GetNumberOfWrongAttemptsToLockTheAccount = 4,
            TokenRotationGracePeriodMinutes = 2,
            IsOidcEnabled = true,
            PasswordPolicyMessage = "msg"
        };

        private void Stored(string clientId, OidcClientTokenLifetimes? value) =>
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(clientId))
                .ReturnsAsync(new OidcClientRegistration { ItemId = clientId, ClientId = clientId, TokenLifetimes = value });

        // ---------- ApplyAsync: unchanged project instance (C1, C2) ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Apply_NoClientId_ReturnsSameInstance_WithoutAnyRead(string? clientId)
        {
            var project = Project();

            var result = await Sut().ApplyAsync(project, clientId);

            result.Should().BeSameAs(project);
            _repo.Verify(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Apply_ClientWithoutEntry_ReturnsSameInstance()
        {
            var project = Project();
            Stored("cid", null);

            var result = await Sut().ApplyAsync(project, "cid");

            result.Should().BeSameAs(project);
            _repo.Verify(r => r.GetOidcClientRegistrationAsync("cid"), Times.Once);
        }

        [Fact]
        public async Task Apply_EntryWithNoValue_ReturnsSameInstance()
        {
            var project = Project();
            Stored("cid", new OidcClientTokenLifetimes());

            (await Sut().ApplyAsync(project, "cid")).Should().BeSameAs(project);
        }

        [Fact]
        public async Task Apply_NullProject_NoEntry_StaysNull()
        {
            Stored("cid", null);

            (await Sut().ApplyAsync(null, "cid")).Should().BeNull();
        }

        // ---------- ApplyAsync: overlay (H9) ----------

        [Fact]
        public async Task Apply_AllValues_ReplacesOnlyTheThreeLifetimes_OnACopy()
        {
            var project = Project();
            Stored("cid", new OidcClientTokenLifetimes
            {
                AccessTokenValidForNumberMinutes = 30,
                RefreshTokenValidForNumberMinutes = 1440,
                AbsoluteRefreshTokenValidForNumberMinutes = 43200
            });

            var result = (await Sut().ApplyAsync(project, "cid"))!;

            result.Should().NotBeSameAs(project);
            result.AccessTokenValidForNumberMinutes.Should().Be(30);
            result.RefreshTokenValidForNumberMinutes.Should().Be(1440);
            result.AbsoluteRefreshTokenValidForNumberMinutes.Should().Be(43200);

            // The project instance is never changed.
            project.AccessTokenValidForNumberMinutes.Should().Be(IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes);
            project.RefreshTokenValidForNumberMinutes.Should().Be(30);
            project.AbsoluteRefreshTokenValidForNumberMinutes.Should().Be(10080);
        }

        [Fact]
        public void Apply_CopyKeepsEveryOtherProjectSetting()
        {
            var project = Project();
            var lifetimes = new OidcClientTokenLifetimes
            {
                AccessTokenValidForNumberMinutes = 30,
                RefreshTokenValidForNumberMinutes = 1440,
                AbsoluteRefreshTokenValidForNumberMinutes = 43200
            };

            var result = OidcClientTokenLifetimeService.Apply(project, lifetimes)!;

            var replaced = new[]
            {
                nameof(IdentityConfiguration.AccessTokenValidForNumberMinutes),
                nameof(IdentityConfiguration.RefreshTokenValidForNumberMinutes),
                nameof(IdentityConfiguration.AbsoluteRefreshTokenValidForNumberMinutes)
            };
            foreach (var property in typeof(IdentityConfiguration).GetProperties().Where(p => p.CanRead && !replaced.Contains(p.Name)))
            {
                property.GetValue(result).Should().Be(property.GetValue(project), $"{property.Name} must be copied unchanged");
            }
        }

        [Fact]
        public void Apply_AccessOnly_KeepsProjectRefreshValues()
        {
            var project = Project();

            var result = OidcClientTokenLifetimeService.Apply(project, new OidcClientTokenLifetimes { AccessTokenValidForNumberMinutes = 15 })!;

            result.AccessTokenValidForNumberMinutes.Should().Be(15);
            result.RefreshTokenValidForNumberMinutes.Should().Be(30);
            result.AbsoluteRefreshTokenValidForNumberMinutes.Should().Be(10080);
        }

        [Fact]
        public void Apply_RefreshOnly_KeepsProjectAccessValue()
        {
            var project = Project();

            var result = OidcClientTokenLifetimeService.Apply(project, new OidcClientTokenLifetimes { RefreshTokenValidForNumberMinutes = 120 })!;

            result.AccessTokenValidForNumberMinutes.Should().Be(project.AccessTokenValidForNumberMinutes);
            result.RefreshTokenValidForNumberMinutes.Should().Be(120);
            result.AbsoluteRefreshTokenValidForNumberMinutes.Should().Be(10080);
        }

        [Fact]
        public void Apply_NullProject_StartsFromDefaults()
        {
            var result = OidcClientTokenLifetimeService.Apply(null, new OidcClientTokenLifetimes { AccessTokenValidForNumberMinutes = 20 })!;

            result.AccessTokenValidForNumberMinutes.Should().Be(20);
            result.RefreshTokenValidForNumberMinutes.Should().Be(IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes);
            result.AbsoluteRefreshTokenValidForNumberMinutes.Should().Be(IdentityConfiguration.DefaultAbsoluteRefreshTokenValidForNumberMinutes);
        }

        // ---------- ApplyAsync: out-of-range stored values (C10) ----------

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(-5, 525601, -1)]
        [InlineData(1441, 0, 525601)]
        public void Apply_StoredValuesOutOfRange_AreIgnored(int access, int sliding, int absolute)
        {
            var project = Project();

            var result = OidcClientTokenLifetimeService.Apply(project, new OidcClientTokenLifetimes
            {
                AccessTokenValidForNumberMinutes = access,
                RefreshTokenValidForNumberMinutes = sliding,
                AbsoluteRefreshTokenValidForNumberMinutes = absolute
            });

            result.Should().BeSameAs(project);
        }

        [Fact]
        public void Apply_OneValueOutOfRange_OthersStillApply()
        {
            var result = OidcClientTokenLifetimeService.Apply(Project(), new OidcClientTokenLifetimes
            {
                AccessTokenValidForNumberMinutes = 0,
                RefreshTokenValidForNumberMinutes = 60
            })!;

            result.AccessTokenValidForNumberMinutes.Should().Be(IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes);
            result.RefreshTokenValidForNumberMinutes.Should().Be(60);
        }

        [Fact]
        public async Task Apply_StoreReadFails_ThrowsInsteadOfFallingBack()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>()))
                .ThrowsAsync(new TimeoutException("store unavailable"));

            await Assert.ThrowsAsync<TimeoutException>(() => Sut().ApplyAsync(Project(), "cid"));
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_UnknownClient_IsReportedAsNone()
        {
            _repo.Setup(r => r.GetOidcClientRegistrationAsync("cid")).ReturnsAsync((OidcClientRegistration)null!);

            (await Sut().GetAsync("cid")).Should().BeNull();
        }

        [Fact]
        public async Task Get_EntryWithoutValues_IsReportedAsNone()
        {
            Stored("cid", new OidcClientTokenLifetimes());

            (await Sut().GetAsync("cid")).Should().BeNull();
        }

        // ---------- Request validation (C3, C4, C5) ----------

        private static SaveOIDCClientRequest WithLifetimes(int? access = null, int? sliding = null, int? absolute = null) => new()
        {
            RedirectUris = ["https://app.example.com/cb"],
            TokenLifetimes = new OidcClientTokenLifetimes
            {
                AccessTokenValidForNumberMinutes = access,
                RefreshTokenValidForNumberMinutes = sliding,
                AbsoluteRefreshTokenValidForNumberMinutes = absolute
            }
        };

        private readonly SaveOIDCClientRequestValidator _validator = new();

        [Fact]
        public void Validate_NoLifetimes_StaysValid()
        {
            _validator.TestValidate(new SaveOIDCClientRequest { RedirectUris = ["https://app.example.com/cb"] })
                .IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(1440, 525600, 525600)]
        [InlineData(30, 1440, 43200)]
        [InlineData(null, 600, 600)]
        public void Validate_ValuesInRange_AreAccepted(int? access, int sliding, int absolute)
        {
            _validator.TestValidate(WithLifetimes(access, sliding, absolute)).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1441)]
        public void Validate_AccessOutOfRange_IsRejected(int access)
        {
            _validator.TestValidate(WithLifetimes(access: access))
                .ShouldHaveValidationErrorFor("TokenLifetimes.AccessTokenValidForNumberMinutes")
                .WithErrorMessage(SaveOIDCClientRequestValidator.AccessTokenLifetimeMessage);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(525601)]
        public void Validate_RefreshValuesOutOfRange_AreRejected(int value)
        {
            _validator.TestValidate(WithLifetimes(sliding: value))
                .ShouldHaveValidationErrorFor("TokenLifetimes.RefreshTokenValidForNumberMinutes")
                .WithErrorMessage(SaveOIDCClientRequestValidator.RefreshTokenLifetimeMessage);
            _validator.TestValidate(WithLifetimes(absolute: value))
                .ShouldHaveValidationErrorFor("TokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes")
                .WithErrorMessage(SaveOIDCClientRequestValidator.AbsoluteRefreshTokenLifetimeMessage);
        }

        [Fact]
        public void Validate_AbsoluteBelowSliding_IsRejectedOnAbsolute()
        {
            var result = _validator.TestValidate(WithLifetimes(sliding: 600, absolute: 599));

            result.ShouldHaveValidationErrorFor("TokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes")
                .WithErrorMessage(SaveOIDCClientRequestValidator.AbsoluteBelowSlidingMessage);
            result.Errors.Should().ContainSingle();
        }
    }
}
