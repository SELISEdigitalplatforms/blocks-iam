using Blocks.Genesis;
using Blocks.Secrets;
using FluentAssertions;
using Mfa.DomainService.TOTP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XUnitTest.TestDoubles;

namespace XUnitTest.Mfa.TOTP
{
    /// <summary>
    /// <see cref="TotpSecretResolver"/>: the value lives only in Blocks Secrets, an anonymous sign-in
    /// check reads in a service context, and every failure is closed.
    /// </summary>
    public sealed class TotpSecretResolverTests : IDisposable
    {
        private const string TenantId = "tenant-a";
        private const string Reference = "ref-totp-1";
        private const string Value = "JBSWY3DPEHPK3PXP";

        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);

        public TotpSecretResolverTests()
        {
            BlocksContext.IsTestMode = true;
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null, false);
            BlocksContext.IsTestMode = false;
        }

        private TotpSecretResolver Resolver()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => _secrets.Object);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            return new TotpSecretResolver(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TotpSecretResolver>.Instance);
        }

        private static BlocksContext UserContext() =>
            BlocksContext.Create(
                tenantId: TenantId, roles: [], userId: "u1", isAuthenticated: true,
                requestUri: "/mfa/totp/setup", organizationId: "org-1", expireOn: DateTime.UtcNow.AddMinutes(5),
                email: string.Empty, permissions: [], userName: "u1", phoneNumber: string.Empty,
                displayName: "u1", oauthToken: string.Empty, originalTenantId: TenantId);

        [Fact]
        public async Task AnonymousSignInCheck_ReadsInAServiceContextForTheAmbientTenant_AndRestoresIt()
        {
            var ambient = IdentityProviderSecretTestDoubles.AnonymousContext(TenantId);
            BlocksContext.SetContext(ambient, false);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(Value);

            (await Resolver().GetAsync("u1", Reference)).Should().Be(Value);

            seen!.IsAuthenticated.Should().BeTrue();
            seen.TenantId.Should().Be(TenantId);
            seen.UserId.Should().Be(TotpSecretResolver.ServiceUserId);
            BlocksContext.GetContext().Should().BeSameAs(ambient);
        }

        [Fact]
        public async Task SignedInCaller_ReadsInItsOwnContext()
        {
            BlocksContext.SetContext(UserContext(), false);
            BlocksContext? seen = null;
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .Callback(() => seen = BlocksContext.GetContext())
                .ReturnsAsync(Value);

            await Resolver().GetAsync("u1", Reference);

            seen!.UserId.Should().Be("u1");
        }

        [Fact]
        public async Task NoTenant_FailsClosed_WithoutAStoreCall()
        {
            BlocksContext.SetContext(null, false);

            var act = () => Resolver().GetAsync("u1", Reference);

            (await act.Should().ThrowAsync<TotpSecretUnavailableException>()).Which.FailureClass.Should().Be(TotpSecretResolver.NoTenantFailure);
            _secrets.Verify(s => s.GetValueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LockedSecret_FailsClosed_AndTheMessageHoldsNoReferenceOrValue()
        {
            BlocksContext.SetContext(UserContext(), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretStateException(SecretStatuses.Locked, "read value", "locked " + Reference));

            var act = () => Resolver().GetAsync("u1", Reference);

            var ex = (await act.Should().ThrowAsync<TotpSecretUnavailableException>()).Which;
            ex.FailureClass.Should().Be(nameof(SecretStateException));
            ex.Message.Should().NotContain(Reference).And.NotContain(Value);
        }

        [Fact]
        public async Task EmptyValue_FailsClosed()
        {
            BlocksContext.SetContext(UserContext(), false);
            _secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(string.Empty);

            var act = () => Resolver().GetAsync("u1", Reference);

            await act.Should().ThrowAsync<TotpSecretUnavailableException>();
        }

        [Fact]
        public async Task Store_WritesAServiceSecretUnderTheGivenOrganization()
        {
            BlocksContext.SetContext(UserContext(), false);
            SetSecretRequest? sent = null;
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SetSecretRequest, CancellationToken>((r, _) => sent = r)
                .ReturnsAsync(Reference);

            (await Resolver().StoreAsync("u1", "org-9", Value)).Should().Be(Reference);

            sent!.Type.Should().Be(SecretTypes.Service);
            sent.Name.Should().Be(TotpSecretResolver.SecretName);
            sent.OrganizationId.Should().Be("org-9");
            sent.Value.Should().Be(Value);
            sent.Description.Should().NotContain(Value);
        }

        [Fact]
        public async Task Store_StoreFailure_FailsClosed()
        {
            BlocksContext.SetContext(UserContext(), false);
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("vault down"));

            var act = () => Resolver().StoreAsync("u1", null, Value);

            await act.Should().ThrowAsync<TotpSecretUnavailableException>();
        }

        [Fact]
        public async Task Retire_DeletesTheSecret_AndIgnoresOneAlreadyGone()
        {
            BlocksContext.SetContext(UserContext(), false);
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).ThrowsAsync(new SecretNotFoundException(Reference));

            await Resolver().RetireAsync("u1", Reference);

            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Retire_NoReference_MakesNoStoreCall()
        {
            BlocksContext.SetContext(UserContext(), false);

            await Resolver().RetireAsync("u1", null);

            _secrets.VerifyNoOtherCalls();
        }
    }
}
