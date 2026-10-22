using Api.Controllers;
using Authentication.DomainService.Authentication;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.DelegatedAdministration;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// A delegated-administration refusal reaches the caller as 403 with <c>Access_Grant_Not_Allowed</c>
    /// on every IAM endpoint, and any other failure keeps its 400.
    /// </summary>
    public class IamControllerDelegationTests
    {
        private readonly Mock<IAccountService> _accountService = new();
        private readonly Mock<IUserManagementQueryService> _userQuery = new();
        private readonly Mock<IUserManagementMutationService> _userMutation = new();
        private readonly Mock<IResourceMutationService> _resourceMutation = new();
        private readonly Mock<IResourceQueryService> _resourceQuery = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IOrganizationNameResolver> _organizationNameResolver = new();

        private static BaseMutationResponse Refused() =>
            new() { IsSuccess = false, Errors = DelegatedAdministrationErrors.Create() };

        private IamController CreateController() =>
            new(_accountService.Object, _resourceMutation.Object, _resourceQuery.Object, _userQuery.Object,
                _userMutation.Object, _authService.Object, _organizationNameResolver.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

        private static void Forbidden(IActionResult result) =>
            result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        [Fact]
        public async Task EveryMutationEndpointAnswersARefusalWith403()
        {
            _resourceMutation.Setup(s => s.CreatePermissionAsync(It.IsAny<CreatePermissionRequest>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.UpdatePermissionAsync(It.IsAny<string>(), It.IsAny<UpdatePermissionRequest>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.ArchivePermissionAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.UpdateRoleAsync(It.IsAny<UpdateRoleRequest>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.ArchiveRoleAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.SetRolesAsync(It.IsAny<SetRolesRequest>()))
                .ReturnsAsync(new SetRolesResponse { Errors = DelegatedAdministrationErrors.Create() });
            _userMutation.Setup(s => s.CreateUserAsync(It.IsAny<CreateUserRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.UpdateUserAsync(It.IsAny<UpdateUserRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.DeactivateUserAsync(It.IsAny<DeactivateUserRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.ActivateUserAsync(It.IsAny<ActivateUserByAdminRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.UpdateUserAccessControlAsync(It.IsAny<UpdateUserAccessControlRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.RevokeUserAccessControlAsync(It.IsAny<RevokeUserAccessControlRequest>())).ReturnsAsync(Refused());
            _userMutation.Setup(s => s.UpdateMyAccountAsync(It.IsAny<UpdateMyAccountRequest>())).ReturnsAsync(Refused());
            var controller = CreateController();

            Forbidden(await controller.CreatePermission(new CreatePermissionRequest()));
            Forbidden(await controller.UpdatePermission("p1", new UpdatePermissionRequest()));
            Forbidden(await controller.ArchivePermission("p1"));
            Forbidden(await controller.CreateRole(new CreateRoleRequest()));
            Forbidden(await controller.UpdateRole(new UpdateRoleRequest()));
            Forbidden(await controller.ArchiveRole("r1"));
            Forbidden(await controller.AssignRolePermissions(new SetRolesRequest { Slug = "r" }));
            Forbidden(await controller.CreateUser(new CreateUserRequest()));
            Forbidden(await controller.UpdateUser("u1", new UpdateUserRequest()));
            Forbidden(await controller.DeactivateUser(new DeactivateUserRequest { UserId = "u1" }));
            Forbidden(await controller.ActivateUser(new ActivateUserByAdminRequest { UserId = "u1", Reason = "reinstate" }));
            Forbidden(await controller.UpdateUserAccessControl(new UpdateUserAccessControlRequest { UserId = "u1" }));
            Forbidden(await controller.RevokeUserAccessControl(new RevokeUserAccessControlRequest { UserId = "u1" }));
            Forbidden(await controller.UpdateMyAccount(new UpdateMyAccountRequest()));
        }

        [Fact]
        public async Task BulkRoleChangeRefusalIs403OnPreviewAndSubmit()
        {
            _userMutation.Setup(s => s.PreviewBulkRoleChangeAsync(It.IsAny<BulkRoleChangeRequest>()))
                .ReturnsAsync(new BulkRoleChangePreviewResponse { Errors = DelegatedAdministrationErrors.Create() });
            _userMutation.Setup(s => s.SubmitBulkRoleChangeAsync(It.IsAny<BulkRoleChangeRequest>()))
                .ReturnsAsync(new BulkRoleChangeSubmitResponse { Errors = DelegatedAdministrationErrors.Create() });
            var controller = CreateController();

            Forbidden(await controller.PreviewBulkRoleChange(new BulkRoleChangeRequest()));
            Forbidden(await controller.SubmitBulkRoleChange(new BulkRoleChangeRequest()));
        }

        [Fact]
        public async Task SystemOnlyEndpointsAnswerARefusalWith403()
        {
            _resourceMutation.Setup(s => s.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), It.IsAny<string>())).ReturnsAsync(Refused());
            _resourceMutation.Setup(s => s.SaveOrganizationConfigAsync(It.IsAny<SaveOrganizationConfigRequest>()))
                .ReturnsAsync(new BaseResponse { Errors = DelegatedAdministrationErrors.Create() });
            _accountService.Setup(s => s.SaveSignUpSettingAsync(It.IsAny<SaveSignUpSettingRequest>()))
                .ReturnsAsync(new SaveSignUpSettingResponse { Errors = DelegatedAdministrationErrors.Create() });
            var controller = CreateController();

            (await controller.CreateOrganization(new CreateOrganizationRequest { Name = "Acme" })).Result
                .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            (await controller.SaveOrganizationConfig(new SaveOrganizationConfigRequest())).Result
                .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            (await controller.SaveSignUpSetting(new SaveSignUpSettingRequest())).Result
                .Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        [Fact]
        public async Task AnyOtherFailureKeepsItsBadRequest()
        {
            _resourceMutation.Setup(s => s.CreateRoleAsync(It.IsAny<CreateRoleRequest>()))
                .ReturnsAsync(new BaseMutationResponse { Errors = new Dictionary<string, string> { { "forbidden", "Multi_Org_Required_For_Organization_Role" } } });

            var result = await CreateController().CreateRole(new CreateRoleRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task DelegationScopeComesFromTheService()
        {
            var service = new Mock<IDelegatedAdministrationService>();
            var response = new DelegationScopeResponse { IsSystemUser = false, HeldRoles = ["manager"] };
            service.Setup(s => s.GetDelegationScopeAsync()).ReturnsAsync(response);

            var result = await CreateController().GetDelegationScope(service.Object);

            result.Should().BeSameAs(response);
        }
    }
}
