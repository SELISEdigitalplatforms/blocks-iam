using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Shared
{
    public sealed class OidcTemplateManagementServiceTests
    {
        private readonly Mock<IMessageClient> _message = new();
        private readonly Mock<IAuthenticationRepository> _repository = new();
        private readonly Mock<IValidator<SaveOIDCClientRequest>> _oidcClientValidator = new();
        private readonly Mock<IValidator<SaveOidcUiTemplateRequest>> _templateValidator = new();
        private readonly Mock<IValidator<SaveIdentityProviderRequest>> _saveIdpValidator = new();
        private readonly Mock<IValidator<UpdateIdentityProviderRequest>> _updateIdpValidator = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IHttpClientFactory> _httpClientFactory = new();

        public OidcTemplateManagementServiceTests()
        {
            _templateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<SaveOidcUiTemplateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        private AuthenticationDomainService Create() => new(
            _message.Object,
            _repository.Object,
            _oidcClientValidator.Object,
            _templateValidator.Object,
            _saveIdpValidator.Object,
            _updateIdpValidator.Object,
            _tenants.Object,
            _httpClientFactory.Object);

        [Fact]
        public async Task ManagementGet_WithoutSavedTemplate_ReturnsNull()
        {
            _repository.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync((OidcUiTemplate?)null);

            var result = await Create().GetOidcTemplateForManagementAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task ManagementGet_ReturnsStoredTemplateWithoutChangingIt()
        {
            var stored = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding { BrandName = "Acme" },
                Theme = new OidcUiTemplateTheme { Primary = "#123456" },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage { Heading = "Welcome" }
                }
            };
            _repository.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync(stored);

            var result = await Create().GetOidcTemplateForManagementAsync();

            result.Should().BeSameAs(stored);
        }

        [Fact]
        public async Task Save_ValidRequest_PersistsCompleteTemplateAndReturnsItsGeneratedItemId()
        {
            OidcUiTemplate? persisted = null;
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Callback<OidcUiTemplate>(value => persisted = value)
                .Returns(Task.CompletedTask);
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.BrandName = "Acme";

            var result = await Create().SaveOidcUiTemplateRequestAsync(request);

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrWhiteSpace();
            Guid.TryParse(result.ItemId, out _).Should().BeTrue();
            persisted.Should().NotBeNull();
            persisted!.ItemId.Should().Be(result.ItemId);
            persisted.SchemaVersion.Should().Be(OidcUiTemplate.CurrentSchemaVersion);
            persisted.Branding.Should().BeSameAs(request.Branding);
            persisted.Theme.Should().BeSameAs(request.Theme);
            persisted.Pages.Should().BeSameAs(request.Pages);
        }

        [Fact]
        public async Task Save_OmittedOptionalFields_PersistsThemAsNull()
        {
            OidcUiTemplate? persisted = null;
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Callback<OidcUiTemplate>(value => persisted = value)
                .Returns(Task.CompletedTask);
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.LogoUrl = null;
            request.Pages!.Mfa!.ResendButton = null;
            request.Pages.AccountSelector!.Subheading = null;

            var result = await Create().SaveOidcUiTemplateRequestAsync(request);

            result.IsSuccess.Should().BeTrue();
            persisted!.Branding!.LogoUrl.Should().BeNull();
            persisted.Pages!.Mfa!.ResendButton.Should().BeNull();
            persisted.Pages.AccountSelector!.Subheading.Should().BeNull();
        }

        [Fact]
        public async Task Save_InvalidRequest_ReturnsEveryValidationErrorAndDoesNotPersist()
        {
            _templateValidator
                .Setup(v => v.ValidateAsync(It.IsAny<SaveOidcUiTemplateRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(
                [
                    new ValidationFailure("Theme.Dark.Primary", "invalid color"),
                    new ValidationFailure("Pages.Login.Heading", "too long"),
                    new ValidationFailure("Branding.BrandName", "required")
                ]));

            var result = await Create().SaveOidcUiTemplateRequestAsync(OidcUiTemplateTestData.ValidRequest());

            result.IsSuccess.Should().BeFalse();
            result.ItemId.Should().BeNull();
            result.Errors.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["Theme.Dark.Primary"] = "invalid color",
                ["Pages.Login.Heading"] = "too long",
                ["Branding.BrandName"] = "required"
            });
            _repository.Verify(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()), Times.Never);
        }

        [Fact]
        public async Task Save_WhenRepositoryWriteFails_ReturnsFailureWithoutAnItemId()
        {
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .ThrowsAsync(new InvalidOperationException("Mongo unavailable"));

            var result = await Create().SaveOidcUiTemplateRequestAsync(OidcUiTemplateTestData.ValidRequest());

            result.IsSuccess.Should().BeFalse();
            result.ItemId.Should().BeNull();
            result.Errors.Should().ContainKey("Template");
        }

        [Fact]
        public async Task Save_IsImmediatelyVisibleThroughManagementAndPublicReads()
        {
            OidcUiTemplate? stored = null;
            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Callback<OidcUiTemplate>(value => stored = value)
                .Returns(Task.CompletedTask);
            _repository.Setup(r => r.GetOidcUiTemplateAsync()).ReturnsAsync(() => stored);
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.BrandName = "Visible immediately";
            request.Pages!.Login!.Heading = "New login heading";
            var service = Create();

            var save = await service.SaveOidcUiTemplateRequestAsync(request);
            var management = await service.GetOidcTemplateForManagementAsync();

            var httpService = new Mock<IHttpService>();
            var idpService = new IdpService(
                _repository.Object,
                Mock.Of<IAuthorizationCodeRepository>(),
                Mock.Of<IAuthenticationFlowService>(),
                Mock.Of<ICacheClient>(),
                new IdpTokenExchangeClient(httpService.Object),
                _tenants.Object,
                Mock.Of<ICaptchaConfigurationRepository>(),
                NullLogger<IdpService>.Instance);
            var publicResult = await idpService.GetUiConfigAsync();
            var publicConfig = ((OkObjectResult)publicResult).Value.Should().BeOfType<OidcUiConfigResponse>().Subject;

            save.IsSuccess.Should().BeTrue();
            management.Branding!.BrandName.Should().Be("Visible immediately");
            management.Pages!.Login!.Heading.Should().Be("New login heading");
            publicConfig.Template.Should().BeEquivalentTo(management);
        }

        [Fact]
        public async Task ConcurrentSaves_AreIndependentAndLastCompletedWriteWins()
        {
            OidcUiTemplate? stored = null;
            var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            _repository.Setup(r => r.SaveOidcUiTemplateAsync(It.IsAny<OidcUiTemplate>()))
                .Returns<OidcUiTemplate>(async template =>
                {
                    if (template.Branding!.BrandName == "First")
                    {
                        firstEntered.SetResult();
                        await allowFirstToFinish.Task;
                    }

                    stored = template;
                });

            var firstRequest = OidcUiTemplateTestData.ValidRequest();
            firstRequest.Branding!.BrandName = "First";
            var secondRequest = OidcUiTemplateTestData.ValidRequest();
            secondRequest.Branding!.BrandName = "Second";
            var service = Create();

            var firstTask = service.SaveOidcUiTemplateRequestAsync(firstRequest);
            await firstEntered.Task;
            var secondResult = await service.SaveOidcUiTemplateRequestAsync(secondRequest);
            allowFirstToFinish.SetResult();
            var firstResult = await firstTask;

            firstResult.IsSuccess.Should().BeTrue();
            secondResult.IsSuccess.Should().BeTrue();
            firstResult.ItemId.Should().NotBe(secondResult.ItemId);
            stored!.Branding!.BrandName.Should().Be("First", "the write completing last replaces the earlier value");
            stored.ItemId.Should().Be(firstResult.ItemId);
        }
    }
}
