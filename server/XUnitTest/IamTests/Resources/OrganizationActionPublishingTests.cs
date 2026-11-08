using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Actions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace XUnitTest.IamTests.Resources
{
    /// <summary>
    /// IAM tells the tenant's webhooks about an organization change only after it was saved, and the
    /// message never carries the organization name (it can be a person's name).
    /// </summary>
    public sealed class OrganizationActionPublishingTests : IDisposable
    {
        private const string OrgA = "org-a";

        private readonly Mock<IResourceRepository> _repo = new();
        private readonly Mock<IActionPublisher> _actions = new();
        private readonly List<(string Action, string Json)> _published = new();

        public OrganizationActionPublishingTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "actor-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(
                new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromCloud = true });
            _repo.Setup(r => r.SaveOrganizationAsync(It.IsAny<Organization>())).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetOrganizationById(OrgA)).ReturnsAsync(new Organization { ItemId = OrgA, Name = "Old" });
            _repo.Setup(r => r.GetOrganizationByIdForView(OrgA)).ReturnsAsync(new Organization { ItemId = OrgA, Name = "Old" });

            _actions.Setup(a => a.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?, string?, CancellationToken>((name, data, _, _) =>
                    _published.Add((name, JsonSerializer.Serialize(data))))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private ResourceMutationService Create()
        {
            var iam = new Mock<IIdentityAccessManagementService>();
            iam.Setup(i => i.SendToQueueAsync(It.IsAny<string>(), It.IsAny<object>())).Returns(Task.CompletedTask);
            var activity = new Mock<IUserActivityDispatcher>();
            activity.Setup(a => a.SendUserActivityAsync(It.IsAny<UserActivityEvent>())).Returns(Task.CompletedTask);
            return new(NullLogger<ResourceMutationService>.Instance, _repo.Object, iam.Object,
                new Mock<IValidator<CreatePermissionRequest>>().Object, new Mock<IValidator<UpdatePermissionRequest>>().Object,
                new Mock<IValidator<CreateRoleRequest>>().Object, Mock.Of<ITenantPermissionPropagator>(), activity.Object,
                Mock.Of<IUserRepository>(), actionPublisher: _actions.Object);
        }

        [Fact]
        public async Task CreateOrganization_PublishesCreatedWithItsOwnIdAndNoName()
        {
            var result = await Create().CreateOrganizationAsync(
                new CreateOrganizationRequest { Name = "Jane Doe", DefaultRoleForMembers = ["member"], CreatedFrom = CreatedFrom.Cloud });

            result.IsSuccess.Should().BeTrue();
            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.OrganizationCreated,
                $"{{\"organizationId\":\"{result.ItemId}\",\"createdFrom\":\"Cloud\",\"defaultRoleForMembers\":[\"member\"]}}"));
            _published[0].Json.Should().NotContain("Jane");
        }

        [Fact]
        public async Task CreateOrganization_WhenRefused_PublishesNothing()
        {
            _repo.Setup(r => r.GetTenantConfigurationAsync()).ReturnsAsync(
                new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowOrgCreationFromCloud = false });

            var result = await Create().CreateOrganizationAsync(new CreateOrganizationRequest { Name = "N", CreatedFrom = CreatedFrom.Cloud });

            result.IsSuccess.Should().BeFalse();
            _published.Should().BeEmpty();
        }

        [Fact]
        public async Task UpdateOrganization_PublishesUpdatedWithItsOwnId()
        {
            var result = await Create().UpdateOrganizationAsync(OrgA, new SaveOrganizationRequest { Name = "New", IsEnable = false });

            result.IsSuccess.Should().BeTrue();
            _published.Should().ContainSingle().Which.Should().Be((IamActionNames.OrganizationUpdated, "{\"organizationId\":\"org-a\",\"isEnable\":false}"));
        }

        [Fact]
        public async Task UpdateOrganization_WhenRefused_PublishesNothing()
        {
            var result = await Create().UpdateOrganizationAsync("default", new SaveOrganizationRequest { Name = "N" });

            result.IsSuccess.Should().BeFalse();
            _published.Should().BeEmpty();
        }
    }
}
