using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Blocks.Secrets;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using XUnitTest.TestDoubles;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// Client-credential secrets in Blocks Secrets: create stores the secret and keeps a reference,
    /// an edit moves a plaintext secret unchanged, rotate replaces it, delete retires it, and a
    /// write that does not land leaves no orphan and no dangling reference.
    /// </summary>
    public sealed class ClientCredentialSecretTests : IDisposable
    {
        private const string Reference = "cs-ref-1";
        private const string LegacyValue = "blxk_legacy-plaintext";
        private const string StoreUnavailable = "secret_store_unavailable";

        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<ISecretService> _secrets = new(MockBehavior.Strict);
        private readonly CapturingLogger<AuthenticationDomainService> _logger = new();

        public ClientCredentialSecretTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private AuthenticationDomainService Create() =>
            new(Mock.Of<IMessageClient>(), _repo.Object,
                new AuthenticationRequestValidators(
                    Mock.Of<IValidator<SaveOIDCClientRequest>>(), Mock.Of<IValidator<SaveOidcUiTemplateRequest>>(),
                    Mock.Of<IValidator<SaveIdentityProviderRequest>>(), Mock.Of<IValidator<UpdateIdentityProviderRequest>>()),
                Mock.Of<IHttpClientFactory>(), IdentityProviderSecretTestDoubles.Resolver(),
                ClientCredentialSecretTestDoubles.Resolver(_secrets), _logger);

        private static SaveClientCredentialRequest NewCredential() => new() { Name = "svc", Roles = ["r"] };

        private static SaveClientCredentialRequest Edit() => new() { ItemId = "cc-1", Name = "renamed", Roles = ["r"] };

        private static ClientCredential Legacy() =>
            new() { ItemId = "cc-1", Name = "svc", OrganizationId = "default", ClientSecret = LegacyValue, IsActive = true };

        private static ClientCredential Referenced() =>
            new() { ItemId = "cc-1", Name = "svc", OrganizationId = "default", ClientSecret = string.Empty, ClientSecretReference = Reference, IsActive = true };

        private static SecretVaultException VaultDown() => new("down " + Reference, "set", Reference);

        private void StoreAccepts(string? value = null) =>
            _secrets.Setup(s => s.SetAsync(It.Is<SetSecretRequest>(r => value == null || r.Value == value), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Reference);

        private void DeleteAccepted() =>
            _secrets.Setup(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        private void SaveReturns(bool acknowledged) =>
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ReturnsAsync(new BaseResponse { IsSuccess = acknowledged });

        // ---------- create ----------

        [Fact]
        public async Task Create_StoreDown_ReturnsSecretStoreUnavailable_AndWritesNothing()
        {
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(VaultDown());

            var result = await Create().SaveClientCredentialAsync(NewCredential());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(StoreUnavailable);
            result.ClientSecret.Should().BeNull();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task Create_SaveThrows_RetiresTheNewSecret_AndRethrows()
        {
            StoreAccepts();
            DeleteAccepted();
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ThrowsAsync(new TimeoutException());
            _repo.Setup(r => r.GetClientCredentialByIdAsync(It.IsAny<string>())).ReturnsAsync((ClientCredential)null!);

            var act = () => Create().SaveClientCredentialAsync(NewCredential());

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Create_SaveThrows_ButTheDocumentLanded_KeepsTheSecret()
        {
            StoreAccepts();
            _repo.Setup(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>())).ThrowsAsync(new TimeoutException());
            _repo.Setup(r => r.GetClientCredentialByIdAsync(It.IsAny<string>())).ReturnsAsync(Referenced());

            var act = () => Create().SaveClientCredentialAsync(NewCredential());

            await act.Should().ThrowAsync<TimeoutException>();
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Create_SaveNotAcknowledged_RetiresTheNewSecret_AndReturnsNoSecret()
        {
            StoreAccepts();
            DeleteAccepted();
            SaveReturns(acknowledged: false);
            _repo.Setup(r => r.GetClientCredentialByIdAsync(It.IsAny<string>())).ReturnsAsync((ClientCredential)null!);

            var result = await Create().SaveClientCredentialAsync(NewCredential());

            result.IsSuccess.Should().BeFalse();
            result.ClientSecret.Should().BeNull();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------- edit ----------

        [Fact]
        public async Task Edit_LegacyCredential_MovesTheSameValueToTheStore()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy());
            StoreAccepts(LegacyValue);
            SaveReturns(acknowledged: true);

            var result = await Create().SaveClientCredentialAsync(Edit());

            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().BeNull();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c =>
                c.Name == "renamed" && c.ClientSecret == string.Empty && c.ClientSecretReference == Reference)), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.Is<SetSecretRequest>(r => r.Value == LegacyValue), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Edit_ReferencedCredential_MakesNoSecretCall()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced());
            SaveReturns(acknowledged: true);

            var result = await Create().SaveClientCredentialAsync(Edit());

            result.IsSuccess.Should().BeTrue();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c => c.ClientSecretReference == Reference)), Times.Once);
            _secrets.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Edit_StoreDown_ReturnsSecretStoreUnavailable_AndWritesNothing()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy());
            _secrets.Setup(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(VaultDown());

            var result = await Create().SaveClientCredentialAsync(Edit());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(StoreUnavailable);
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task Edit_SaveNotAcknowledged_RetiresTheMovedSecret_WhenTheStoredDocumentStillHasThePlaintext()
        {
            _repo.SetupSequence(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy()).ReturnsAsync(Legacy());
            StoreAccepts(LegacyValue);
            DeleteAccepted();
            SaveReturns(acknowledged: false);

            var result = await Create().SaveClientCredentialAsync(Edit());

            result.IsSuccess.Should().BeFalse();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Edit_SaveNotAcknowledged_AndTheDocumentCannotBeReadBack_KeepsTheMovedSecret()
        {
            // Orphan over dangling: a read rule can hide the document after an update.
            _repo.SetupSequence(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy()).ReturnsAsync((ClientCredential)null!);
            StoreAccepts(LegacyValue);
            SaveReturns(acknowledged: false);

            await Create().SaveClientCredentialAsync(Edit());

            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---------- rotate ----------

        [Fact]
        public async Task Rotate_ReferencedCredential_RotatesInPlace_AndKeepsTheReference()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced());
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            SaveReturns(acknowledged: true);

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeTrue();
            result.ClientSecret.Should().StartWith("blxk_");
            _secrets.Verify(s => s.RotateAsync(Reference, It.Is<RotateSecretRequest>(r => r.Value == result.ClientSecret), It.IsAny<CancellationToken>()), Times.Once);
            _secrets.Verify(s => s.SetAsync(It.IsAny<SetSecretRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            _repo.Verify(r => r.SaveClientCredentialAsync(It.Is<ClientCredential>(c => c.ClientSecretReference == Reference && c.ClientSecret == string.Empty)), Times.Once);
        }

        [Fact]
        public async Task Rotate_StoreDown_ReturnsSecretStoreUnavailable_AndWritesNothing()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced());
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(VaultDown());

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey(StoreUnavailable);
            result.ClientSecret.Should().BeNull();
            _repo.Verify(r => r.SaveClientCredentialAsync(It.IsAny<ClientCredential>()), Times.Never);
        }

        [Fact]
        public async Task Rotate_LegacyCredential_SaveNotAcknowledged_RetiresTheNewSecret_SoTheOldPlaintextStaysValid()
        {
            _repo.SetupSequence(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy()).ReturnsAsync(Legacy());
            StoreAccepts();
            DeleteAccepted();
            SaveReturns(acknowledged: false);

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeFalse();
            result.ClientSecret.Should().BeNull();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Rotate_InPlace_SaveNotAcknowledged_LogsThatTheRotationCannotBeUndone()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced());
            _secrets.Setup(s => s.RotateAsync(Reference, It.IsAny<RotateSecretRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            SaveReturns(acknowledged: false);

            var result = await Create().RotateClientCredentialSecretAsync("cc-1");

            result.IsSuccess.Should().BeFalse();
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("cannot be undone"));
            _logger.Entries.Should().OnlyContain(e => !e.AllText.Contains(Reference));
        }

        // ---------- delete ----------

        [Fact]
        public async Task Delete_RetiresTheSecret_OnceTheDocumentIsGone()
        {
            _repo.SetupSequence(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced()).ReturnsAsync((ClientCredential)null!);
            _repo.Setup(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>())).Returns(Task.CompletedTask);
            DeleteAccepted();

            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.DeleteAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Delete_DocumentStillThere_KeepsTheSecret()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Referenced());
            _repo.Setup(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>())).Returns(Task.CompletedTask);

            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });

            result.IsSuccess.Should().BeTrue();
            _secrets.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Delete_LegacyCredential_MakesNoSecretCall()
        {
            _repo.Setup(r => r.GetClientCredentialByIdAsync("cc-1")).ReturnsAsync(Legacy());
            _repo.Setup(r => r.DeleteClientCredentialAsync(It.IsAny<DeleteClientCredentialRequest>())).Returns(Task.CompletedTask);

            var result = await Create().DeleteClientCredentialAsync(new DeleteClientCredentialRequest { ItemId = "cc-1" });

            result.IsSuccess.Should().BeTrue();
            _secrets.VerifyNoOtherCalls();
        }

        // ---------- list ----------

        [Fact]
        public async Task List_ReturnsNeitherTheSecretNorTheReference()
        {
            _repo.Setup(r => r.GetClientCredentialsAsync(It.IsAny<string?>())).ReturnsAsync([Referenced(), Legacy()]);

            var list = await Create().GetClientCredentialsAsync(new GetAllClientCredentialsRequest());

            list.Should().HaveCount(2);
            list.Should().OnlyContain(c => c.ClientSecret == null && c.ClientSecretReference == null);
            _secrets.VerifyNoOtherCalls();
        }
    }
}
