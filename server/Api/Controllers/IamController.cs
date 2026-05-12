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
using Iam.DomainService.Shared.Entities;
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

        /// <summary>
        /// Get user sessions
        /// Retrieves all active sessions for authenticated user
        /// Shows device information, login time, and activity timestamps
        /// </summary>
        /// <param name="query">Query parameters for session filtering and pagination</param>
        /// <returns>List of user sessions with metadata</returns>
        /// <response code="200">Successfully retrieved sessions</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("sessions")]
        [ProtectedEndPoint("blocks-idp::iam::getsessions")]
        public async Task<GetSessionsResponse> GetSessions([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetSessionsAsync(query);
        }

        /// <summary>
        /// Get user activity history
        /// Retrieves audit log of user actions, logins, and API calls
        /// Shows complete activity trail with timestamps
        /// </summary>
        /// <param name="query">Query parameters for activity filtering and date range</param>
        /// <returns>List of activity history entries</returns>
        /// <response code="200">Successfully retrieved activity history</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("history")]
        [ProtectedEndPoint("blocks-idp::iam::gethistories")]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        [HttpPost("permissions/create")]
        [ProtectedEndPoint("blocks-idp::createpermission")]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions/{id}")]
        [ProtectedEndPoint("blocks-idp::updatepermission")]
        public async Task<IActionResult> UpdatePermission([FromRoute] string id, [FromBody] UpdatePermissionRequest command)
        {
            command.ItemId = id;
            var result = await _resourceMutationService.UpdatePermissionAsync(id, command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/create")]
        [ProtectedEndPoint("blocks-idp::createrole")]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles/{id}")]
        [ProtectedEndPoint("blocks-idp::updaterole")]
        public async Task<IActionResult> UpdateRole([FromRoute] string id, [FromBody] UpdateRoleRequest command)
        {
            command.ItemId = id;
            var result = await _resourceMutationService.UpdateRoleAsync(id, command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("permissions")]
        [ProtectedEndPoint("blocks-idp::getpermissions")]
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
        [ProtectedEndPoint("blocks-idp::getpermission")]
        public async Task<GetPermissionResponse> GetPermission([FromRoute] string id)
        {
            return await _resourceQueryService.GetPermissionAsync(id);
        }

        [HttpPost("roles")]
        [ProtectedEndPoint("blocks-idp::iam::getroles")]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            return await _resourceQueryService.GetRolesAsync(query);
        }

        [HttpGet("roles/{id}")]
        [ProtectedEndPoint("blocks-idp::getrole")]
        public async Task<GetRoleResponse> GetRole([FromRoute] string id)
        {
            return await _resourceQueryService.GetRoleAsync(id);
        }

        [HttpPost("roles/assign")]
        [ProtectedEndPoint("blocks-idp::setroles")]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("resource-groups")]
        [ProtectedEndPoint("blocks-idp::getresourcegroups")]
        public async Task<List<GetResourceGroupResponse>> GetResourceGroups([FromQuery] GetResourceGroupRequest request)
        {
            return await _resourceQueryService.GetResourceGroupsAsync();
        }

        #endregion

        #region User

        /// <summary>
        /// Create new user account
        /// Registers new user with email, name, and optional roles
        /// Sends activation email if email verification is required
        /// </summary>
        /// <param name="command">User creation request with profile and roles</param>
        /// <returns>Created user with ID and profile information</returns>
        /// <response code="200">User created successfully</response>
        /// <response code="400">Invalid user data or duplicate email</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("users/create")]
        [ProtectedEndPoint("blocks-idp::iam::create")]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
        {
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("users")]
        [ProtectedEndPoint("blocks-idp::iam::create")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest command)
        {
            var result = await _userManagementMutationService.CreateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Update user account information
        /// Modifies user profile, email, name, and other metadata
        /// Does not modify user's roles (use Assign Roles endpoint)
        /// </summary>
        /// <param name="command">User update request with new profile data</param>
        /// <returns>Updated user information</returns>
        /// <response code="200">User updated successfully</response>
        /// <response code="400">Invalid update data or user not found</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("users/update")]
        [ProtectedEndPoint("blocks-idp::iam::update")]
        public async Task<IActionResult> Update([FromBody] UpdateUserRequest command)
        {
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPatch("users/{id}")]
        [ProtectedEndPoint("blocks-idp::iam::update")]
        public async Task<IActionResult> UpdateUser([FromRoute] string id, [FromBody] UpdateUserRequest command)
        {
            command.ItemId = id;
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Deactivate user account
        /// Revokes all active sessions and disables login
        /// User data is preserved for re-activation
        /// </summary>
        /// <param name="request">Request with user ID to deactivate</param>
        /// <returns>Deactivation confirmation</returns>
        /// <response code="200">User deactivated successfully</response>
        /// <response code="400">User not found or already inactive</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("users/deactivate")]
        [Authorize]
        public async Task<IActionResult> Deactivate([FromBody] DeactivateUserRequest request)
        {
            var result = await _userManagementMutationService.DeactivateUserAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPatch("users/{id}/deactivate")]
        [Authorize]
        public async Task<IActionResult> DeactivateUser([FromRoute] string id)
        {
            var result = await _userManagementMutationService.DeactivateUserAsync(new DeactivateUserRequest
            {
                UserId = id
            });
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Update authenticated user's own account
        /// Allows users to modify their profile without admin privileges
        /// Restricted to user's own account only
        /// </summary>
        /// <param name="command">Update request with user's new profile data</param>
        /// <returns>Updated user information</returns>
        /// <response code="200">Account updated successfully</response>
        /// <response code="400">Invalid update data</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("account/update")]
        [ProtectedEndPoint("blocks-idp::iam::updateaccount")]
        public async Task<IActionResult> UpdateAccount([FromBody] UpdateUserRequest command)
        {
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get users list with filtering
        /// Retrieves multiple users with pagination and search
        /// Shows user profiles with status and role assignments
        /// </summary>
        /// <param name="query">Query filters, pagination, and search criteria</param>
        /// <returns>Paginated users list</returns>
        /// <response code="200">Successfully retrieved users</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("users/search")]
        [ProtectedEndPoint("blocks-idp::iam::getusers")]
        public async Task<GetUsersResponse> SearchUsers([FromBody] GetUsersRequest query)
        {
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        [HttpGet("users")]
        [ProtectedEndPoint("blocks-idp::iam::getusers")]
        public async Task<GetUsersResponse> GetUsers([FromQuery] GetUsersRequest query)
        {
            return await _userManagementQueryService.GetUsersAsync(query);
        }

        /// <summary>
        /// Get single user details
        /// Retrieves complete user profile information
        /// Shows all roles and basic permissions
        /// </summary>
        /// <param name="query">Request with user ID</param>
        /// <returns>Detailed user information with profile</returns>
        /// <response code="200">Successfully retrieved user</response>
        /// <response code="401">Authentication required</response>
        /// <response code="404">User not found</response>
        [HttpGet("user")]
        [ProtectedEndPoint("blocks-idp::iam::getuser")]
        public async Task<GetUserResponse> GetUser([FromQuery] GetUserRequest query)
        {
            return await _userManagementQueryService.GetUserAsync(query.Id);
        }

        [HttpGet("users/{id}")]
        [ProtectedEndPoint("blocks-idp::iam::getuser")]
        public async Task<GetUserResponse> GetUserById([FromRoute] string id)
        {
            return await _userManagementQueryService.GetUserAsync(id);
        }

        /// <summary>
        /// Get roles assigned to user
        /// Retrieves all roles with detailed permission lists
        /// Shows complete permission set inherited from roles
        /// </summary>
        /// <param name="query">Request with user ID</param>
        /// <returns>List of user's roles with permissions</returns>
        /// <response code="200">Successfully retrieved user roles</response>
        /// <response code="401">Authentication required</response>
        /// <summary>
        /// Get accounts list (organizations the user belongs to)
        /// Retrieves all organizations/accounts the user has access to
        /// Shows membership status and roles in each account
        /// </summary>
        /// <param name="query">Query filters and pagination options</param>
        /// <returns>List of user's accounts with roles</returns>
        /// <response code="200">Successfully retrieved accounts</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("accounts")]
        [ProtectedEndPoint("blocks-idp::iam::getaccounts")]
        public async Task<GetAccountsResponse> GetAccounts([FromBody] GetAccountsRequest query)
        {
            return await _userManagementQueryService.GetAccountsAsync(query);
        }

        /// <summary>
        /// Get authenticated user's current account details
        /// Retrieves account information for the authenticated user
        /// Shows current organization membership
        /// </summary>
        /// <returns>Current account information and profile</returns>
        /// <response code="200">Successfully retrieved account</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("account")]
        [ProtectedEndPoint("blocks-idp::iam::getaccount")]
        public async Task<GetAccountResponse> GetAccount()
        {
            return await _userManagementQueryService.GetAccountAsync();
        }

        /// <summary>
        /// Get authenticated user's roles in current account
        /// Retrieves roles with full permission details
        /// Shows effective permissions in current context
        /// </summary>
        /// <returns>User's roles in current account</returns>
        /// <response code="200">Successfully retrieved account roles</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("me")]
        [ProtectedEndPoint("blocks-idp::iam::getaccount")]
        public async Task<GetAccountResponse> GetMyAccount()
        {
            return await _userManagementQueryService.GetAccountAsync();
        }

        [HttpPatch("me")]
        [ProtectedEndPoint("blocks-idp::iam::updateaccount")]
        public async Task<IActionResult> UpdateMyAccount([FromBody] UpdateUserRequest command)
        {
            var bc = BlocksContext.GetContext();
            command.ItemId = bc?.UserId ?? command.ItemId;
            var result = await _userManagementMutationService.UpdateUserAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Save roles and permissions configuration
        /// Bulk operation to set roles and permissions in single call
        /// Use for migration or batch updates
        /// </summary>
        /// <param name="command">Configuration with roles and permissions</param>
        /// <returns>Update result and summary</returns>
        /// <response code="200">Roles and permissions saved successfully</response>
        /// <response code="400">Invalid configuration</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("users/access")]
        [ProtectedEndPoint("blocks-idp::iam::saverolesandpermissions")]
        public async Task<IActionResult> SaveUserAccess(SaveRolesAndPermissionsRequest command)
        {
            var result = await _userManagementMutationService.SaveRolesAndPermissionsAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpPost("roles-permissions")]
        [ProtectedEndPoint("blocks-idp::iam::saverolesandpermissions")]
        public async Task<IActionResult> SaveRolesAndPermissions(SaveRolesAndPermissionsRequest command)
        {
            var result = await _userManagementMutationService.SaveRolesAndPermissionsAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Check if email address is available for registration
        /// Validates email uniqueness across the system
        /// Use for real-time form validation
        /// </summary>
        /// <param name="query">Request with email to check</param>
        /// <returns>Availability status (available/taken)</returns>
        /// <response code="200">Email availability status returned</response>
        /// <response code="400">Invalid email format</response>
        [HttpGet("email/available")]
        public async Task<IActionResult> IsEmailAvaiable([FromQuery] IsEmailAvaiableRequest query)
        {
            var result = await _userManagementQueryService.IsUserAvailableAsync(query);
            return Ok(new IsEmailAvaiableResponse
            {
                IsAvailable = result
            });
        }

        /// <summary>
        /// Get user activity timeline (audit log)
        /// Retrieves chronological record of user's actions
        /// Shows logins, API calls, permission changes, etc.
        /// </summary>
        /// <param name="request">Query parameters for time range and filters</param>
        /// <returns>List of user timeline events</returns>
        /// <response code="200">Successfully retrieved user timeline</response>
        /// <response code="401">Authentication required</response>
        [Authorize]
        [ProtectedEndPoint("blocks-idp::iam::getusertimelines")]
        [HttpGet("user/timelines")]
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

        [HttpPost("organizations")]
        [Authorize]
        public async Task<GetOrganizationsResponse> GetOrganizations([FromBody] GetOrganizationsRequest request)
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
        [ProtectedEndPoint("blocks-idp::save-signup-setting")]
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
        [ProtectedEndPoint("blocks-idp::save-iam-configuration")]
        public async Task<IActionResult> Save([FromBody] SaveIamConfigurationRequest request)
        {
            var result = await _configurationService.SaveIamConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("config")]
        [ProtectedEndPoint("blocks-idp::get-iam-configuration")]
        public async Task<GetConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            return await _configurationService.GetIamConfigurationAsync();
        }
        #endregion
    }
}
