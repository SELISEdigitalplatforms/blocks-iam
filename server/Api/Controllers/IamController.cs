using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Authentication.RequestModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.RequestModel;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("iam")]

    public class IamController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly IUserActivityService _userActivityService;
        private readonly IUserManagementQueryService _userManagementQueryService;
        private readonly IUserManagementMutationService _userManagementMutationService;
        private readonly IResourceMutationService _resourceMutationService;
        private readonly IResourceQueryService _resourceQueryService;
        private readonly IConfigurationService _configurationService;

        public IamController(IAccountService accountService,
                             IUserActivityService userActivityService,
                             IResourceMutationService resourceMutationService,
                             IResourceQueryService resourceQueryService,
                             IUserManagementQueryService userManagementQueryService,
                             IUserManagementMutationService userManagementMutationService, IConfigurationService configurationService)
        {
            _userActivityService = userActivityService;
            _resourceMutationService = resourceMutationService;
            _resourceQueryService = resourceQueryService;
            _userManagementQueryService = userManagementQueryService;
            _userManagementMutationService = userManagementMutationService;
            _accountService = accountService;
            _configurationService = configurationService;
        }



        #region Activity

        [HttpGet("sessions")]
        //[ProtectedEndPoint("blocks-idp::get-sessions")]
        public async Task<GetSessionsResponse> GetSessions([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetSessionsAsync(query);
        }

        [HttpGet("history")]
        //[ProtectedEndPoint("blocks-idp::get-histories")]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        [HttpPost("permissions/create")]
        //[ProtectedEndPoint("blocks-idp::create-permission")]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions/{id}")]
        //[ProtectedEndPoint("blocks-idp::update-permission")]
        public async Task<IActionResult> UpdatePermission([FromRoute] string id, [FromBody] UpdatePermissionRequest command)
        {
            command.ItemId = id;
            var result = await _resourceMutationService.UpdatePermissionAsync(id, command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/create")]
        //[ProtectedEndPoint("blocks-idp::create-role")]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/{id}")]
        //[ProtectedEndPoint("blocks-idp::update-role")]
        public async Task<IActionResult> UpdateRole([FromRoute] string id, [FromBody] UpdateRoleRequest command)
        {
            command.ItemId = id;
            var result = await _resourceMutationService.UpdateRoleAsync(id, command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions")]
        //[ProtectedEndPoint("blocks-idp::get-permissions")]
        public async Task<GetPermissionsResponse> GetPermissions([FromBody] GetPermissionsRequest query)
        {
            return await _resourceQueryService.GetPermissionsAsync(query);
        }

        [HttpGet("permissions/by-severity")]
        [Authorize]
        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverity([FromQuery] GetPermissionGroupBySeverityRequest request)
        {
            return await _resourceQueryService.GetPermissionsGroupBySeverityAsync();
        }

        [HttpGet("permissions/{id}")]
        //[ProtectedEndPoint("blocks-idp::get-permission")]
        public async Task<GetPermissionResponse> GetPermission([FromRoute] string id)
        {
            return await _resourceQueryService.GetPermissionAsync(id);
        }

        [HttpPost("roles")]
        //[ProtectedEndPoint("blocks-idp::get-roles")]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            return await _resourceQueryService.GetRolesAsync(query);
        }

        [HttpGet("roles/{id}")]
        //[ProtectedEndPoint("blocks-idp::get-role")]
        public async Task<GetRoleResponse> GetRole([FromRoute] string id)
        {
            return await _resourceQueryService.GetRoleAsync(id);
        }

        [HttpPost("roles/assign-permissions")]
        //[ProtectedEndPoint("blocks-idp::assign-roles-to-permission")]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions/assign-org")]
        //[ProtectedEndPoint("blocks-idp::assign-permissions-to-organization")]
        public async Task<IActionResult> AssignPermissionsToOrganization([FromBody] AssignPermissionsToOrganizationRequest command)
        {
            var result = await _resourceMutationService.AssignPermissionsToOrganizationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/assign-org")]
        //[ProtectedEndPoint("blocks-idp::assign-roles-to-organization")]
        public async Task<IActionResult> AssignRolesToOrganization([FromBody] AssignRolesToOrganizationRequest command)
        {
            var result = await _resourceMutationService.AssignRolesToOrganizationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("resource-groups")]
        //[ProtectedEndPoint("blocks-idp::get-resource-groups")]
        public async Task<List<GetResourceGroupResponse>> GetResourceGroups([FromQuery] GetResourceGroupRequest request)
        {
            return await _resourceQueryService.GetResourceGroupsAsync();
        }

        #endregion

        #region User

        [HttpPost("users/create")]
        //[ProtectedEndPoint("blocks-idp::create-user")]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
        {
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/{id}")]
        //[ProtectedEndPoint("blocks-idp::update-user")]
        public async Task<IActionResult> Update([FromRoute] string id, [FromBody] UpdateUserRequest command)
        {
            command.ItemId = id;
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/deactivate")]
        [Authorize]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateUserRequest request)
        {
            var result = await _userManagementMutationService.DeactivateUserAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("users")]
        //[ProtectedEndPoint("blocks-idp::get-users")]
        public async Task<GetUsersResponse> GetUsers([FromQuery] GetUsersRequest query)
        {
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        [HttpGet("users/{id}")]
        //[ProtectedEndPoint("blocks-idp::get-user")]
        public async Task<GetUserResponse> GetUser([FromRoute] string id)
        {
            return await _userManagementQueryService.GetUserAsync(id);
        }

        [HttpGet("me")]
        //[ProtectedEndPoint("blocks-idp::get-my-account")]
        [Authorize]
        public async Task<GetAccountResponse> GetMyAccount()
        {
            DomainResolver.ResetToOriginalBlocksContextForImpersonation();
            return await _userManagementQueryService.GetAccountAsync();
        }

        [HttpPatch("me")]
        //[ProtectedEndPoint("blocks-idp::update-my-account")]
        [Authorize]
        public async Task<IActionResult> UpdateMyAccount([FromBody] UpdateUserRequest command)
        {
            DomainResolver.ResetToOriginalBlocksContextForImpersonation();
            var bc = BlocksContext.GetContext();
            command.ItemId = bc?.UserId;
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/roles-and-permissions")]
        //[ProtectedEndPoint("blocks-idp::role-and-permission-management")]
        public async Task<IActionResult> SaveRolesAndPermissions(SaveRolesAndPermissionsRequest command)
        {
            var result = await _userManagementMutationService.SaveRolesAndPermissionsAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/org-update")]
        //[ProtectedEndPoint("blocks-idp::update-organization-user")]
        public async Task<IActionResult> UpdateOrganizationUser(UpdateOrganizationUserRequest command)
        {
            var result = await _userManagementMutationService.UpdateOrganizationUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("email/available")]
        public async Task<IActionResult> IsEmailAvailable([FromQuery] IsEmailAvailableRequest query)
        {
            var result = await _userManagementQueryService.IsUserAvailableAsync(query);
            return Ok(new IsEmailAvailableResponse
            {
                IsAvailable = result
            });
        }

        [HttpGet("users/timeline")]
        //[ProtectedEndPoint("blocks-idp::users-timeline")]
        public async Task<List<UserTimeline>> GetUserTimelines(GetUserTimeLineRequest request)
        {
            return await _userManagementQueryService.GetUserTimelinesAsync(request);
        }

        #endregion

        #region Organization

        [HttpPost("organizations/create")]
        [Authorize]
        public async Task<BaseMutationResponse> CreateOrganization([FromBody] CreateOrganizationRequest request)
        {
            return await _resourceMutationService.CreateOrganizationAsync(request);
        }

        [HttpPost("organizations/{id}")]
        [Authorize]
        public async Task<BaseResponse> UpdateOrganization([FromRoute] string id, [FromBody]  SaveOrganizationRequest request)
        {
            return await _resourceMutationService.UpdateOrganizationAsync(id, request);
        }

        [HttpGet("organizations")]
        [Authorize]
        public async Task<GetOrganizationsResponse> GetOrganizations([FromQuery] GetOrganizationsRequest request)
        {
            return await _resourceMutationService.GetOrganizationsAsync(request);
        }

        [HttpGet("organizations/{id}")]
        [Authorize]
        public async Task<GetOrganizationResponse> GetOrganization([FromRoute]  string id)
        {
            return await _resourceMutationService.GetOrganizationAsync(id);
        }

        [HttpPost("organizations/config")]
        [Authorize]
        public async Task<BaseResponse> SaveOrganizationConfig([FromBody] SaveOrganizationConfigRequest request)
        {
            return await _resourceMutationService.SaveOrganizationConfigAsync(request);
        }

        [HttpGet("organizations/config")]
        [Authorize]
        public async Task<Dictionary<string, object>> GetOrganizationConfig()
        {
            return await _resourceMutationService.GetOrganizationConfigAsync();
        }

        [HttpPost("signup-settings")]
        //[ProtectedEndPoint("blocks-idp::save-signup-setting")]
        public async Task<SaveSignUpSettingResponse> SaveSignUpSetting([FromBody] SaveSignUpSettingRequest request)
        {
            return await _accountService.SaveSignUpSettingAsync(request);
        }

        [HttpGet("signup-settings")]
        public async Task<Dictionary<string, object>> GetSignUpSetting()
        {
            return await _accountService.GetSignUpSettingAsync();
        }

        #endregion
        #region Cloud configuration
        [HttpPost("config")]
        //[ProtectedEndPoint("blocks-idp::save-iam-configuration")]
        public async Task<IActionResult> Save([FromBody] SaveIamConfigurationRequest request)
        {
            var result = await _configurationService.SaveIamConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("config")]
        //[ProtectedEndPoint("blocks-idp::get-iam-configuration")]
        public async Task<GetConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            return await _configurationService.GetIamConfigurationAsync();
        }
        #endregion
    }
}
