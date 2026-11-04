using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// R8: a client credential carries only what the caller could assign, and changing, rotating or
    /// deleting one is managing whatever it carries. The caller "caller" holds "manager" (a descendant).
    /// </summary>
    public sealed class ClientCredentialGrantGuardTests : IDisposable
    {
        private const string Org = AccessGrantTestKit.Org;

        private readonly AccessGrantTestKit _kit = new AccessGrantTestKit().WithStandardTree();
        private readonly Mock<IAuthenticationRepository> _credentials = new();

        public ClientCredentialGrantGuardTests()
        {
            _kit.WithUser("caller", ["manager"]);
            AccessGrantTestKit.SignIn("caller");
        }

        public void Dispose() => AccessGrantTestKit.SignOut();

        private ClientCredentialGrantGuard Guard() => new(_credentials.Object, _kit.Service());

        private void Stored(string id, string org, List<string> roles, List<string>? permissions = null) =>
            _credentials.Setup(c => c.GetClientCredentialByIdAsync(id))
                .ReturnsAsync(new ClientCredential { ItemId = id, OrganizationId = org, Roles = roles, Permissions = permissions ?? [] });

        [Fact]
        public async Task CreateCarriesOnlyWhatTheCallerCouldAssign()
        {
            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { Name = "svc", Roles = ["viewer"] })).Should().BeTrue();
            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { Name = "svc", Permissions = ["write"] }))
                .Should().BeFalse("a descendant gives power through roles, never as a direct permission");
            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { Name = "svc", Roles = ["owner"] })).Should().BeFalse();
            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { Name = "svc", Permissions = ["billing"] })).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAlsoNeedsPowerOverWhatTheCredentialAlreadyCarries()
        {
            Stored("cc-1", Org, ["owner"]);

            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { ItemId = "cc-1", Name = "svc", Roles = ["viewer"] })).Should().BeFalse();
        }

        [Fact]
        public async Task RotateAndDeleteAreManagingTheCredential()
        {
            Stored("cc-high", Org, ["admin"]);
            Stored("cc-low", Org, ["viewer"], ["read"]);

            (await Guard().MayChangeAsync("cc-high")).Should().BeFalse();
            (await Guard().MayChangeAsync("cc-low")).Should().BeTrue();
        }

        [Fact]
        public async Task MissingOrForeignCredentialsAreLeftToTheDomainServiceNotFound()
        {
            Stored("cc-foreign", "org-other", ["owner"]);

            (await Guard().MayChangeAsync("cc-missing")).Should().BeTrue();
            (await Guard().MayChangeAsync("cc-foreign")).Should().BeTrue();
            (await Guard().MayChangeAsync(null)).Should().BeTrue();
        }

        [Fact]
        public async Task SystemUserIsNeverChecked()
        {
            AccessGrantTestKit.SignIn("caller", "default");
            Stored("cc-1", "default", ["owner"]);

            (await Guard().MaySaveAsync(new SaveClientCredentialRequest { ItemId = "cc-1", Name = "svc", Roles = ["anything"] })).Should().BeTrue();
            (await Guard().MayChangeAsync("cc-1")).Should().BeTrue();
            _credentials.Verify(c => c.GetClientCredentialByIdAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
