using Authentication.DomainService.Authentication;
using Authentication.DomainService.Utilities;
using Azure.Core;
using Blocks.Genesis;
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
        private readonly IAuthenticationService _authenticationService;

        public IamController(IAccountService accountService,
                             IUserActivityService userActivityService,
                             IResourceMutationService resourceMutationService,
                             IResourceQueryService resourceQueryService,
                             IUserManagementQueryService userManagementQueryService,
                             IUserManagementMutationService userManagementMutationService,
                             IAuthenticationService authenticationService)
        {
            _userActivityService = userActivityService;
            _resourceMutationService = resourceMutationService;
            _resourceQueryService = resourceQueryService;
            _userManagementQueryService = userManagementQueryService;
            _userManagementMutationService = userManagementMutationService;
            _accountService = accountService;
            _authenticationService = authenticationService;
        }



        #region Activity

        [HttpGet("sessions")]
        //[ProtectedEndPoint("blocks-idp::get-sessions")]
        [Authorize]
        public async Task<GetSessionsResponse> GetSessions([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetSessionsAsync(query);
        }

        [HttpGet("history")]
        //[ProtectedEndPoint("blocks-idp::get-histories")]
        [Authorize]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        [HttpPost("permissions/create")]
        //[ProtectedEndPoint("blocks-idp::create-permission")]
        [Authorize]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions/{id}")]
        //[ProtectedEndPoint("blocks-idp::update-permission")]
        [Authorize]
        public async Task<IActionResult> UpdatePermission([FromRoute] string id, [FromBody] UpdatePermissionRequest command)
        {
            command.ItemId = id;
            var result = await _resourceMutationService.UpdatePermissionAsync(id, command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/create")]
        //[ProtectedEndPoint("blocks-idp::create-role")]
        [Authorize]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/update")]
        //[ProtectedEndPoint("blocks-idp::update-role")]
        [Authorize]
        public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest command)
        {
            var result = await _resourceMutationService.UpdateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions")]
        //[ProtectedEndPoint("blocks-idp::get-permissions")]
        [Authorize]
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
        [Authorize]
        public async Task<GetPermissionResponse> GetPermission([FromRoute] string id)
        {
            return await _resourceQueryService.GetPermissionAsync(id);
        }

        [HttpPost("roles")]
        //[ProtectedEndPoint("blocks-idp::get-roles")]
        [Authorize]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            return await _resourceQueryService.GetRolesAsync(query);
        }

        [HttpGet("roles/{id}")]
        //[ProtectedEndPoint("blocks-idp::get-role")]
        [Authorize]
        public async Task<GetRoleResponse> GetRole([FromRoute] string id)
        {
            return await _resourceQueryService.GetRoleAsync(id);
        }

        [HttpPost("roles/assign-permissions")]
        //[ProtectedEndPoint("blocks-idp::assign-roles-to-permission")]
        [Authorize]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("roles/assignable")]
        //[ProtectedEndPoint("blocks-idp::get-assignable-roles")]
        [Authorize]
        public async Task<GetAssignableRolesResponse> GetAssignableRoles()
        {
            return await _resourceQueryService.GetAssignableRolesAsync();
        }

        [HttpGet("resource-groups")]
        //[ProtectedEndPoint("blocks-idp::get-resource-groups")]
        [Authorize]
        public async Task<List<GetResourceGroupResponse>> GetResourceGroups([FromQuery] GetResourceGroupRequest request)
        {
            return await _resourceQueryService.GetResourceGroupsAsync();
        }

        [HttpGet("resource/features")]
        [Authorize]
        public async Task<List<GetFeResourceFeatureResponse>> GetFeResourceFeatures([FromQuery] GetFeResourceFeatureRequest request)
        {
            return await _resourceQueryService.GetFeResourceFeaturesAsync(request);
        }

        #endregion

        #region User

        [HttpPost("users/create")]
        //[ProtectedEndPoint("blocks-idp::create-user")]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
        {
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/{id}")]
        //[ProtectedEndPoint("blocks-idp::update-user")]
        [Authorize]
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

        [HttpPost("users")]
        //[ProtectedEndPoint("blocks-idp::get-users")]
        [Authorize]
        public async Task<GetUsersResponse> GetUsers([FromBody] GetUsersRequest query)
        {
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        [HttpGet("users/{id}")]
        //[ProtectedEndPoint("blocks-idp::get-user")]
        [Authorize]
        public async Task<GetUserResponse> GetUser([FromRoute] string id, [FromQuery] string? organizationId)
        {
            return await _userManagementQueryService.GetUserAsync(id, organizationId);
        }

        [HttpGet("me")]
        //[ProtectedEndPoint("blocks-idp::get-my-account")]
        [Authorize]
        public async Task<GetUserResponse> GetMyAccount()
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

        [HttpPost("users/access")]
        //[ProtectedEndPoint("blocks-idp::update-organization-user")]
        [Authorize]
        public async Task<IActionResult> UpdateUserAccessControl(UpdateUserAccessControlRequest command)
        {
            var result = await _userManagementMutationService.UpdateUserAccessControlAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users/revoke-access")]
        //[ProtectedEndPoint("blocks-idp::update-organization-user")]
        [Authorize]
        public async Task<IActionResult> RevokeUserAccessControl(RevokeUserAccessControlRequest command)
        {
            var result = await _userManagementMutationService.RevokeUserAccessControlAsync(command);
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

        [HttpGet("users/exists")]
        [Authorize]
        public async Task<IActionResult> IsUserExist([FromQuery] string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest(new { error = "email is required" });

            var result = await _userManagementQueryService.IsUserExistAsync(email);
            return Ok(result);
        }

        [HttpGet("users/timeline")]
        //[ProtectedEndPoint("blocks-idp::users-timeline")]
        [Authorize]
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
        public async Task<BaseResponse> UpdateOrganization([FromRoute] string id, [FromBody] SaveOrganizationRequest request)
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
        public async Task<GetOrganizationResponse> GetOrganization([FromRoute] string id)
        {
            return await _resourceMutationService.GetOrganizationAsync(id);
        }

        [HttpGet("organizations/my")]
        [Authorize]
        public async Task<GetMyOrganizationsResponse> GetMyOrganization()
        {
            return await _resourceMutationService.GetMyOrganizationAsync();
        }
        #endregion

        #region Config

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
        [Authorize]
        public async Task<SaveSignUpSettingResponse> SaveSignUpSetting([FromBody] SaveSignUpSettingRequest request)
        {
            return await _accountService.SaveSignUpSettingAsync(request);
        }

        [HttpGet("signup-settings")]
        public async Task<Dictionary<string, object>> GetSignUpSetting()
        {
            var userPrincipal = await _authenticationService.GetPrincipalFromTokenAsync(Request, BlocksContext.GetContext()?.TenantId ?? "", IsUserInfoGetRequest: false);

            if (userPrincipal != null)
            {
                bool.TryParse(userPrincipal?.FindFirst("impersonated")?.Value, out bool impersonated);

                if(impersonated)
                {
                    var claimUserId = userPrincipal?.FindFirst("user_id")?.Value;
                    var claimTenantId = userPrincipal?.FindFirst("tenant_id")?.Value;
                    BlocksContext.SetContext(BlocksContext.Create(claimTenantId, [], claimUserId, true, string.Empty, string.Empty, DateTime.MinValue, string.Empty, [], string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
                }
            }
            return await _accountService.GetSignUpSettingAsync();
        }

        #endregion
    }
}
