using Api.Controllers;
using Authentication.DomainService.Authentication;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Reflection;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="IamController"/>. Injected services are mocked; each test asserts
    /// the returned result type/value and verifies the delegated call.
    /// </summary>
    public class IamControllerTests : IDisposable
    {
        private const string ActorUserId = "actor-1";

        private readonly Mock<IAccountService> _accountService = new();
        private readonly Mock<IUserManagementQueryService> _userQuery = new();
        private readonly Mock<IUserManagementMutationService> _userMutation = new();
        private readonly Mock<IResourceMutationService> _resourceMutation = new();
        private readonly Mock<IResourceQueryService> _resourceQuery = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IOrganizationNameResolver> _organizationNameResolver = new();

        public IamControllerTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: ActorUserId, impersonated: false,
                isAuthenticated: true, requestUri: "https://test/iam", organizationId: "default",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private IamController CreateController()
        {
            var controller = new IamController(
                _accountService.Object,
                _resourceMutation.Object,
                _resourceQuery.Object,
                _userQuery.Object,
                _userMutation.Object,
                _authService.Object,
                _organizationNameResolver.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            return controller;
        }

        // ---------- Permissions ----------

        [Fact]
        public async Task CreatePermission_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.CreatePermissionAsync(It.IsAny<CreatePermissionRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().CreatePermission(new CreatePermissionRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CreatePermission_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.CreatePermissionAsync(It.IsAny<CreatePermissionRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().CreatePermission(new CreatePermissionRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task UpdatePermission_Success_SetsItemIdAndReturnsOk()
        {
            _resourceMutation.Setup(s => s.UpdatePermissionAsync("p-1", It.Is<UpdatePermissionRequest>(c => c.ItemId == "p-1")))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().UpdatePermission("p-1", new UpdatePermissionRequest());

            result.Should().BeOfType<OkObjectResult>();
            _resourceMutation.Verify(s => s.UpdatePermissionAsync("p-1", It.Is<UpdatePermissionRequest>(c => c.ItemId == "p-1")), Times.Once);
        }

        [Fact]
        public async Task UpdatePermission_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.UpdatePermissionAsync(It.IsAny<string>(), It.IsAny<UpdatePermissionRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().UpdatePermission("p-1", new UpdatePermissionRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ArchivePermission_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.ArchivePermissionAsync("p-1"))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "p-1" });

            var result = await CreateController().ArchivePermission("p-1");

            result.Should().BeOfType<OkObjectResult>();
            _resourceMutation.Verify(s => s.ArchivePermissionAsync("p-1"), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.ArchivePermissionAsync(It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().ArchivePermission("p-1");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        /// <summary>
        /// Covers C7. Calling the action directly never exercises routing or authorization, so the
        /// two things that actually define this endpoint -- the permission string it is guarded by
        /// and the verb/template it answers on -- are asserted by reflection instead.
        /// </summary>
        [Fact]
        public void ArchivePermission_IsGuardedByMutatePermissionsOnDeleteRoute()
        {
            var method = typeof(IamController).GetMethod(nameof(IamController.ArchivePermission));
            method.Should().NotBeNull();

            var guard = method!.GetCustomAttribute<ProtectedEndPointAttribute>();
            guard.Should().NotBeNull("the archive route must be protected by the same permission as create/update");
            guard!.ResourceName.Should().Be("blocks-iam::iam::mutate-permissions");

            var route = method.GetCustomAttribute<HttpDeleteAttribute>();
            route.Should().NotBeNull("the spec defines the archive route as DELETE");
            route!.Template.Should().Be("permissions/{id}");
        }

        [Fact]
        public void ArchivePermission_UsesTheSamePermissionStringAsCreateAndUpdate()
        {
            static string? PermissionOf(string methodName) =>
                typeof(IamController).GetMethod(methodName)!
                    .GetCustomAttribute<ProtectedEndPointAttribute>()?.ResourceName;

            PermissionOf(nameof(IamController.ArchivePermission))
                .Should().Be(PermissionOf(nameof(IamController.CreatePermission)))
                .And.Be(PermissionOf(nameof(IamController.UpdatePermission)));
        }

        /// <summary>
        /// The consent flag is a defaulted query parameter, so an existing client that omits it
        /// keeps exactly today's behavior. Asserted by reflection because binding is the whole
        /// contract here: a flag bound from the body instead would change the request shape for
        /// every caller.
        /// </summary>
        [Theory]
        [InlineData(nameof(IamController.ArchiveRole))]
        [InlineData(nameof(IamController.ArchivePermission))]
        public void Archive_TakesConsentAsAnOptionalQueryParameterDefaultingToFalse(string methodName)
        {
            var parameter = typeof(IamController).GetMethod(methodName)!
                .GetParameters().SingleOrDefault(p => p.Name == "confirmRevokeFromUsers");

            parameter.Should().NotBeNull("archive must accept explicit consent");
            parameter!.ParameterType.Should().Be(typeof(bool));
            parameter.HasDefaultValue.Should().BeTrue();
            parameter.DefaultValue.Should().Be(false, "omitting the flag must preserve the pre-consent behavior");
            parameter.GetCustomAttribute<FromQueryAttribute>()
                .Should().NotBeNull("the flag is a query parameter, not a body field");
        }

        [Fact]
        public async Task ArchiveRole_ForwardsConsentToTheService()
        {
            _resourceMutation.Setup(s => s.ArchiveRoleAsync("r-1", true))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "r-1" });

            await CreateController().ArchiveRole("r-1", confirmRevokeFromUsers: true);

            _resourceMutation.Verify(s => s.ArchiveRoleAsync("r-1", true), Times.Once);
        }

        [Fact]
        public async Task ArchivePermission_ForwardsConsentToTheService()
        {
            _resourceMutation.Setup(s => s.ArchivePermissionAsync("p-1", true))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "p-1" });

            await CreateController().ArchivePermission("p-1", confirmRevokeFromUsers: true);

            _resourceMutation.Verify(s => s.ArchivePermissionAsync("p-1", true), Times.Once);
        }

        /// <summary>
        /// The impact preview discloses counts about roles and permissions, so it must be guarded
        /// by the same READ permission as the corresponding get endpoint -- not by the mutate one,
        /// which would deny it to callers who are allowed to see the resource, and not by nothing,
        /// which would leak organization and user counts to any authenticated caller.
        /// </summary>
        [Theory]
        [InlineData(nameof(IamController.GetRoleArchiveImpact), nameof(IamController.GetRole), "blocks-iam::iam::roles", "roles/{id}/archive-impact")]
        [InlineData(nameof(IamController.GetPermissionArchiveImpact), nameof(IamController.GetPermission), "blocks-iam::iam::permissions", "permissions/{id}/archive-impact")]
        public void ArchiveImpact_IsGuardedByTheSameReadPermissionOnGetRoute(
            string methodName, string readMethodName, string expectedPermission, string expectedTemplate)
        {
            var method = typeof(IamController).GetMethod(methodName);
            method.Should().NotBeNull();

            var guard = method!.GetCustomAttribute<ProtectedEndPointAttribute>();
            guard.Should().NotBeNull("the impact preview discloses counts and must be protected");
            guard!.ResourceName.Should().Be(expectedPermission);

            // Same string as the plain read endpoint: no new permission resource is introduced,
            // so nothing has to be seeded or propagated for this to work.
            typeof(IamController).GetMethod(readMethodName)!
                .GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName
                .Should().Be(expectedPermission);

            var route = method.GetCustomAttribute<HttpGetAttribute>();
            route.Should().NotBeNull("the impact preview is a read");
            route!.Template.Should().Be(expectedTemplate);
        }

        [Fact]
        public async Task ArchiveRole_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.ArchiveRoleAsync("r-1"))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "r-1" });

            var result = await CreateController().ArchiveRole("r-1");

            result.Should().BeOfType<OkObjectResult>();
            _resourceMutation.Verify(s => s.ArchiveRoleAsync("r-1"), Times.Once);
        }

        [Fact]
        public async Task ArchiveRole_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.ArchiveRoleAsync(It.IsAny<string>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().ArchiveRole("r-1");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        /// <summary>
        /// Covers C8. Invoking the action directly exercises neither routing nor authorization, so
        /// the permission string and the verb/template are asserted by reflection instead — the
        /// same pair used for ArchivePermission.
        /// </summary>
        [Fact]
        public void ArchiveRole_IsGuardedByMutateRolesOnDeleteRoute()
        {
            var method = typeof(IamController).GetMethod(nameof(IamController.ArchiveRole));
            method.Should().NotBeNull();

            var guard = method!.GetCustomAttribute<ProtectedEndPointAttribute>();
            guard.Should().NotBeNull("the archive route must be protected by the same permission as create/update");
            guard!.ResourceName.Should().Be("blocks-iam::iam::mutate-roles");

            var route = method.GetCustomAttribute<HttpDeleteAttribute>();
            route.Should().NotBeNull("the spec defines the archive route as DELETE");
            route!.Template.Should().Be("roles/{id}");
        }

        [Fact]
        public void ArchiveRole_UsesTheSamePermissionStringAsCreateAndUpdate()
        {
            static string? PermissionOf(string methodName) =>
                typeof(IamController).GetMethod(methodName)!
                    .GetCustomAttribute<ProtectedEndPointAttribute>()?.ResourceName;

            PermissionOf(nameof(IamController.ArchiveRole))
                .Should().Be(PermissionOf(nameof(IamController.CreateRole)))
                .And.Be(PermissionOf(nameof(IamController.UpdateRole)));
        }

        [Fact]
        public async Task GetPermissions_DelegatesToQueryService()
        {
            var response = new GetPermissionsResponse();
            _resourceQuery.Setup(s => s.GetPermissionsAsync(It.IsAny<GetPermissionsRequest>())).ReturnsAsync(response);

            var result = await CreateController().GetPermissions(new GetPermissionsRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetPermission_DelegatesToQueryService()
        {
            var response = new GetPermissionResponse();
            _resourceQuery.Setup(s => s.GetPermissionAsync("p-1")).ReturnsAsync(response);

            var result = await CreateController().GetPermission("p-1");

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetPermissionsGroupBySeverity_DelegatesToQueryService()
        {
            var response = new List<PermissionGroupBySeverityResponse>();
            _resourceQuery.Setup(s => s.GetPermissionsGroupBySeverityAsync()).ReturnsAsync(response);

            var result = await CreateController().GetPermissionsGroupBySeverity();

            result.Should().BeSameAs(response);
        }

        // ---------- Roles ----------

        [Fact]
        public async Task CreateRole_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().CreateRole(new CreateRoleRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task CreateRole_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().CreateRole(new CreateRoleRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task UpdateRole_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.UpdateRoleAsync(It.IsAny<UpdateRoleRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().UpdateRole(new UpdateRoleRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetRoles_DelegatesToQueryService()
        {
            var response = new GetRolesResponse();
            _resourceQuery.Setup(s => s.GetRolesAsync(It.IsAny<GetRolesRequest>())).ReturnsAsync(response);

            var result = await CreateController().GetRoles(new GetRolesRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetRole_DelegatesToQueryService()
        {
            var response = new GetRoleResponse();
            _resourceQuery.Setup(s => s.GetRoleAsync("r-1")).ReturnsAsync(response);

            var result = await CreateController().GetRole("r-1");

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task SetRoles_Success_ReturnsOk()
        {
            _resourceMutation.Setup(s => s.SetRolesAsync(It.IsAny<SetRolesRequest>()))
                .ReturnsAsync(new SetRolesResponse { Success = true });

            var result = await CreateController().AssignRolePermissions(new SetRolesRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task SetRoles_Failure_ReturnsBadRequest()
        {
            _resourceMutation.Setup(s => s.SetRolesAsync(It.IsAny<SetRolesRequest>()))
                .ReturnsAsync(new SetRolesResponse { Success = false });

            var result = await CreateController().AssignRolePermissions(new SetRolesRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetAssignableRoles_DelegatesToQueryService()
        {
            var response = new GetAssignableRolesResponse();
            _resourceQuery.Setup(s => s.GetAssignableRolesAsync()).ReturnsAsync(response);

            var result = await CreateController().GetAssignableRoles();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetResourceGroups_DelegatesToQueryService()
        {
            var response = new List<GetResourceGroupResponse>();
            _resourceQuery.Setup(s => s.GetResourceGroupsAsync()).ReturnsAsync(response);

            var result = await CreateController().GetResourceGroups();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetFeResourceFeatures_DelegatesToQueryService()
        {
            var response = new List<GetFeResourceFeatureResponse>();
            _resourceQuery.Setup(s => s.GetFeResourceFeaturesAsync(It.IsAny<GetFeResourceFeatureRequest>())).ReturnsAsync(response);

            var result = await CreateController().GetFeResourceFeatures(new GetFeResourceFeatureRequest());

            result.Should().BeSameAs(response);
        }

        // ---------- Users ----------

        [Fact]
        public async Task Create_Success_ReturnsOk()
        {
            _userMutation.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().CreateUser(new CreateUserRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Create_Failure_ReturnsBadRequest()
        {
            _userMutation.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().CreateUser(new CreateUserRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_Success_SetsItemIdAndReturnsOk()
        {
            _userMutation.Setup(s => s.UpdateUserAsync(It.Is<UpdateUserRequest>(c => c.ItemId == "u-1")))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().UpdateUser("u-1", new UpdateUserRequest());

            result.Should().BeOfType<OkObjectResult>();
            _userMutation.Verify(s => s.UpdateUserAsync(It.Is<UpdateUserRequest>(c => c.ItemId == "u-1")), Times.Once);
        }

        [Fact]
        public async Task Deactivate_Success_ReturnsOk()
        {
            _userMutation.Setup(s => s.DeactivateUserAsync(It.IsAny<DeactivateUserRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateController().DeactivateUser(new DeactivateUserRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Deactivate_Failure_ReturnsBadRequest()
        {
            _userMutation.Setup(s => s.DeactivateUserAsync(It.IsAny<DeactivateUserRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await CreateController().DeactivateUser(new DeactivateUserRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Activate_Success_ReturnsOk()
        {
            _userMutation.Setup(s => s.ActivateUserAsync(It.IsAny<ActivateUserByAdminRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().ActivateUser(new ActivateUserByAdminRequest { UserId = "u-1", Reason = "test" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetUsers_DelegatesToQueryService()
        {
            var response = new GetUsersResponse();
            _userQuery.Setup(s => s.GetUsersAsync(It.IsAny<GetUsersRequest>())).ReturnsAsync(response);

            var result = await CreateController().GetUsers(new GetUsersRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetUser_DelegatesToQueryService()
        {
            var response = new GetUserResponse();
            _userQuery.Setup(s => s.GetUserAsync("u-1", "org-1")).ReturnsAsync(response);

            var result = await CreateController().GetUser("u-1", "org-1");

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetMyAccount_DelegatesToQueryService()
        {
            var response = new GetUserResponse();
            _userQuery.Setup(s => s.GetAccountAsync()).ReturnsAsync(response);

            var result = await CreateController().GetMyAccount();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task UpdateMyAccount_Success_SetsItemIdFromContextAndReturnsOk()
        {
            _userMutation.Setup(s => s.UpdateUserAsync(It.Is<UpdateUserRequest>(c => c.ItemId == ActorUserId)))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().UpdateMyAccount(new UpdateUserRequest());

            result.Should().BeOfType<OkObjectResult>();
            _userMutation.Verify(s => s.UpdateUserAsync(It.Is<UpdateUserRequest>(c => c.ItemId == ActorUserId)), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAccessControl_Success_ReturnsOk()
        {
            _userMutation.Setup(s => s.UpdateUserAccessControlAsync(It.IsAny<UpdateUserAccessControlRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });

            var result = await CreateController().UpdateUserAccessControl(new UpdateUserAccessControlRequest { UserId = "u-1" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task RevokeUserAccessControl_Failure_ReturnsBadRequest()
        {
            _userMutation.Setup(s => s.RevokeUserAccessControlAsync(It.IsAny<RevokeUserAccessControlRequest>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateController().RevokeUserAccessControl(new RevokeUserAccessControlRequest { UserId = "u-1" });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task IsEmailAvailable_ReturnsOk()
        {
            _userQuery.Setup(s => s.IsUserAvailableAsync(It.IsAny<IsEmailAvailableRequest>())).ReturnsAsync(true);

            var result = await CreateController().IsEmailAvailable(new IsEmailAvailableRequest());

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeOfType<IsEmailAvailableResponse>().Which.IsAvailable.Should().BeTrue();
        }

        [Fact]
        public async Task IsUserExist_MissingEmail_ReturnsBadRequest()
        {
            var result = await CreateController().IsUserExist(null);

            result.Should().BeOfType<BadRequestObjectResult>();
            _userQuery.Verify(s => s.IsUserExistAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task IsUserExist_ValidEmail_ReturnsOk()
        {
            _userQuery.Setup(s => s.IsUserExistAsync("a@b.com")).ReturnsAsync(new IsUserExistResponse { UserId = "u-1" });

            var result = await CreateController().IsUserExist("a@b.com");

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- Organizations ----------

        [Fact]
        public async Task IsOrganizationNameAvailable_FreeName_ReturnsAvailableWithNoSuggestions()
        {
            _organizationNameResolver.Setup(r => r.CheckAvailabilityAsync("Acme", It.IsAny<int>()))
                .ReturnsAsync(new OrganizationNameAvailability { MultiOrgEnabled = true, IsAvailable = true });

            var result = await CreateController().IsOrganizationNameAvailable(
                new IsOrganizationNameAvailableRequest { Name = "Acme" });

            var payload = result.Should().BeOfType<OkObjectResult>().Subject
                .Value.Should().BeOfType<IsOrganizationNameAvailableResponse>().Subject;

            payload.IsSuccess.Should().BeTrue();
            payload.IsAvailable.Should().BeTrue();
            payload.Suggestions.Should().BeEmpty();
        }

        [Fact]
        public async Task IsOrganizationNameAvailable_TakenName_ReturnsSuggestions()
        {
            _organizationNameResolver.Setup(r => r.CheckAvailabilityAsync("Acme", It.IsAny<int>()))
                .ReturnsAsync(new OrganizationNameAvailability
                {
                    MultiOrgEnabled = true,
                    IsAvailable = false,
                    Suggestions = new List<string> { "Acme 4821", "Acme 7204" }
                });

            var result = await CreateController().IsOrganizationNameAvailable(
                new IsOrganizationNameAvailableRequest { Name = "Acme" });

            var payload = result.Should().BeOfType<OkObjectResult>().Subject
                .Value.Should().BeOfType<IsOrganizationNameAvailableResponse>().Subject;

            payload.IsAvailable.Should().BeFalse();
            payload.Suggestions.Should().Equal("Acme 4821", "Acme 7204");
        }

        [Fact]
        public async Task IsOrganizationNameAvailable_MultiOrgDisabled_RefusesToAnswer()
        {
            _organizationNameResolver.Setup(r => r.CheckAvailabilityAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new OrganizationNameAvailability { MultiOrgEnabled = false });

            var result = await CreateController().IsOrganizationNameAvailable(
                new IsOrganizationNameAvailableRequest { Name = "Acme" });

            var payload = result.Should().BeOfType<BadRequestObjectResult>().Subject
                .Value.Should().BeOfType<IsOrganizationNameAvailableResponse>().Subject;

            payload.IsSuccess.Should().BeFalse();
            payload.Errors.Should().ContainKey("multi_org_disabled");
            payload.Suggestions.Should().BeEmpty();
        }

        [Fact]
        public async Task CreateOrganization_DelegatesToMutationService()
        {
            var response = new BaseMutationResponse { ItemId = "org-1" };
            _resourceMutation.Setup(s => s.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>()))
                .ReturnsAsync(response);

            var result = await CreateController().CreateOrganization(new CreateOrganizationRequest { Name = "Acme" });

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task UpdateOrganization_DelegatesToMutationService()
        {
            var response = new BaseResponse { IsSuccess = true };
            _resourceMutation.Setup(s => s.UpdateOrganizationAsync("org-1", It.IsAny<SaveOrganizationRequest>()))
                .ReturnsAsync(response);

            var result = await CreateController().UpdateOrganization("org-1", new SaveOrganizationRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetOrganizations_DelegatesToMutationService()
        {
            var response = new GetOrganizationsResponse();
            _resourceMutation.Setup(s => s.GetOrganizationsAsync(It.IsAny<GetOrganizationsRequest>())).ReturnsAsync(response);

            var result = await CreateController().GetOrganizations(new GetOrganizationsRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetOrganization_DelegatesToMutationService()
        {
            var response = new GetOrganizationResponse();
            _resourceMutation.Setup(s => s.GetOrganizationAsync("org-1")).ReturnsAsync(response);

            var result = await CreateController().GetOrganization("org-1");

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetMyOrganization_DelegatesToMutationService()
        {
            var response = new GetMyOrganizationsResponse();
            _resourceMutation.Setup(s => s.GetMyOrganizationAsync()).ReturnsAsync(response);

            var result = await CreateController().GetMyOrganization();

            result.Should().BeSameAs(response);
        }

        // ---------- Config ----------

        [Fact]
        public async Task SaveOrganizationConfig_DelegatesToMutationService()
        {
            var response = new BaseResponse { IsSuccess = true };
            _resourceMutation.Setup(s => s.SaveOrganizationConfigAsync(It.IsAny<SaveOrganizationConfigRequest>())).ReturnsAsync(response);

            var result = await CreateController().SaveOrganizationConfig(new SaveOrganizationConfigRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetOrganizationConfig_DelegatesToMutationService()
        {
            var response = new Dictionary<string, object> { { "k", "v" } };
            _resourceMutation.Setup(s => s.GetOrganizationConfigAsync()).ReturnsAsync(response);

            var result = await CreateController().GetOrganizationConfig();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task SaveSignUpSetting_DelegatesToAccountService()
        {
            var response = new SaveSignUpSettingResponse();
            _accountService.Setup(s => s.SaveSignUpSettingAsync(It.IsAny<SaveSignUpSettingRequest>())).ReturnsAsync(response);

            var result = await CreateController().SaveSignUpSetting(new SaveSignUpSettingRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetSignUpSetting_NoPrincipal_DelegatesToAccountService()
        {
            _authService.Setup(s => s.GetPrincipalFromTokenAsync(It.IsAny<HttpRequest>(), It.IsAny<string>(), false))
                .ReturnsAsync((System.Security.Claims.ClaimsPrincipal)null);
            var response = new Dictionary<string, object> { { "signup", true } };
            _accountService.Setup(s => s.GetSignUpSettingAsync()).ReturnsAsync(response);

            var result = await CreateController().GetSignUpSetting();

            result.Should().BeSameAs(response);
        }

        // ---------------------------------------------------------------------------------
        // #427 — C3: this phase must not change or weaken the existing authorization.
        // ---------------------------------------------------------------------------------

        [Theory]
        [InlineData(nameof(IamController.GetUsers))]
        [InlineData(nameof(IamController.GetUser))]
        public void UsersEndpoints_StillRequireTheIamUsersPermission(string action)
        {
            // The two endpoints #427 touches must keep the permission they had. Adding response
            // fields cannot weaken authz by itself, but the attribute is one careless edit away from
            // the mapper work, and C3 is explicit that authorization is unchanged.
            //
            // Stated limit: this proves the ATTRIBUTE survived, not that an unauthorised caller gets
            // the same status and body - that needs an HTTP boundary, which these tests do not have.
            var method = typeof(IamController).GetMethod(action)!;

            var attribute = method.GetCustomAttributes(typeof(ProtectedEndPointAttribute), true)
                                  .Cast<ProtectedEndPointAttribute>()
                                  .SingleOrDefault();

            attribute.Should().NotBeNull($"{action} must stay behind an explicit permission");
            attribute!.ResourceName.Should().Be("blocks-iam::iam::users");
        }
    }
}
