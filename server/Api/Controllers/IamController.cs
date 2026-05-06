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

        #region Account

        /// <summary>
        /// Activate user account
        /// Validates activation code and marks account as active
        /// User can log in after successful activation
        /// </summary>
        /// <param name="command">Activation request with user email and verification code</param>
        /// <returns>Activation result with user details</returns>
        /// <response code="200">Account activated successfully</response>
        /// <response code="400">Invalid or expired activation code</response>
        [HttpPost("activate")]
        [AllowAnonymous]
        public async Task<IActionResult> Activate([FromBody] ActivateUserRequest command)
        {
            var result = await _accountService.ActivateAccountAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Resend account activation email
        /// Generates new activation code and sends to user's email
        /// Use if user did not receive initial activation email
        /// </summary>
        /// <param name="command">Request with user email to resend activation</param>
        /// <returns>Activation code send result</returns>
        /// <response code="200">Activation email resent successfully</response>
        /// <response code="400">User not found or already activated</response>
        [HttpPost("resend-activation")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendActivation([FromBody] ResendActivationRequest command)
        {
            var result = await _accountService.ResendActivationAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Validate account activation code
        /// Checks if activation code is valid without activating account
        /// Use to verify code before user interaction
        /// </summary>
        /// <param name="command">Request with email and activation code</param>
        /// <returns>Validation result indicating code validity</returns>
        /// <response code="200">Activation code is valid</response>
        /// <response code="400">Invalid or expired activation code</response>
        [HttpPost("validate-activation")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateActivationCode([FromBody] ValidateActivationCodeRequest command)
        {
            var result = await _accountService.ValidateAccountActivationCodeAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        #endregion

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
        [ProtectedEndPoint]
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
        [ProtectedEndPoint]
        public async Task<GetHistorysResponse> GetHistories([FromQuery] BaseActivityRequest query)
        {
            return await _userActivityService.GetHistoriesAsync(query);
        }

        #endregion

        #region Resource

        /// <summary>
        /// Create new permission
        /// Defines granular permissions for role-based access control
        /// Requires admin authorization
        /// </summary>
        /// <param name="command">Permission definition with name, description, severity</param>
        /// <returns>Created permission with ID and metadata</returns>
        /// <response code="200">Permission created successfully</response>
        /// <response code="400">Invalid permission definition or duplicate</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("permissions/create")]
        [ProtectedEndPoint]
        public async Task<IActionResult> CreatePermission([FromBody] CreatePermissionRequest command)
        {
            var result = await _resourceMutationService.CreatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Update existing permission
        /// Modifies permission definition and description
        /// Does not affect already-assigned permissions
        /// </summary>
        /// <param name="command">Permission update request with new details</param>
        /// <returns>Updated permission information</returns>
        /// <response code="200">Permission updated successfully</response>
        /// <response code="400">Invalid update or permission not found</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("permissions/update")]
        [ProtectedEndPoint]
        public async Task<IActionResult> UpdatePermission([FromBody] UpdatePermissionRequest command)
        {
            var result = await _resourceMutationService.UpdatePermissionAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Create new role
        /// Defines role template for user group management
        /// Roles are assigned to users for permission inheritance
        /// </summary>
        /// <param name="command">Role definition with name, description, and default permissions</param>
        /// <returns>Created role with ID and metadata</returns>
        /// <response code="200">Role created successfully</response>
        /// <response code="400">Invalid role definition or duplicate</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("roles/create")]
        [ProtectedEndPoint]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest command)
        {
            var result = await _resourceMutationService.CreateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Update existing role
        /// Modifies role definition and assigned permissions
        /// Changes apply to all users with this role
        /// </summary>
        /// <param name="command">Role update request with new definition</param>
        /// <returns>Updated role information</returns>
        /// <response code="200">Role updated successfully</response>
        /// <response code="400">Invalid update or role not found</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("roles/update")]
        [ProtectedEndPoint]
        public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest command)
        {
            var result = await _resourceMutationService.UpdateRoleAsync(command);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get permissions list
        /// Retrieves all system permissions with filtering and pagination
        /// Returns permission metadata and severity levels
        /// </summary>
        /// <param name="query">Query filters by name, category, or severity</param>
        /// <returns>Paginated permissions list</returns>
        /// <response code="200">Successfully retrieved permissions</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("permissions")]
        [ProtectedEndPoint]
        public async Task<GetPermissionsResponse> GetPermissions([FromBody] GetPermissionsRequest query)
        {
            return await _resourceQueryService.GetPermissionsAsync(query);
        }

        /// <summary>
        /// Get permissions grouped by severity level
        /// Returns permissions organized by risk/importance classification
        /// Useful for UI display in permission selection dialogs
        /// </summary>
        /// <returns>Permissions grouped by severity (critical, high, medium, low)</returns>
        /// <response code="200">Successfully retrieved grouped permissions</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("permissions/by-severity")]
        [Authorize]
        public async Task<List<PermissionGroupBySeverityResponse>> GetPermissionsGroupBySeverity([FromQuery] GetPermissionGroupBySeverityRequest request)
        {
            return await _resourceQueryService.GetPermissionsGroupBySeverityAsync();
        }

        /// <summary>
        /// Get single permission details
        /// Retrieves detailed information about specific permission
        /// Shows description, severity, and assigned roles
        /// </summary>
        /// <param name="query">Request with permission ID</param>
        /// <returns>Detailed permission information</returns>
        /// <response code="200">Successfully retrieved permission</response>
        /// <response code="401">Authentication required</response>
        /// <response code="404">Permission not found</response>
        [HttpGet("permission")]
        [ProtectedEndPoint]
        public async Task<GetPermissionResponse> GetPermission([FromQuery] GetPermissionRequest query)
        {
            return await _resourceQueryService.GetPermissionAsync(query.Id);
        }

        /// <summary>
        /// Get roles list
        /// Retrieves all roles with filtering and pagination
        /// Shows role definitions and permission assignments
        /// </summary>
        /// <param name="query">Query filters and pagination options</param>
        /// <returns>Paginated roles list</returns>
        /// <response code="200">Successfully retrieved roles</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("roles")]
        [ProtectedEndPoint]
        public async Task<GetRolesResponse> GetRoles([FromBody] GetRolesRequest query)
        {
            return await _resourceQueryService.GetRolesAsync(query);
        }

        /// <summary>
        /// Get single role details
        /// Retrieves detailed information about specific role
        /// Shows all assigned permissions and member count
        /// </summary>
        /// <param name="query">Request with role ID</param>
        /// <returns>Detailed role information with permissions</returns>
        /// <response code="200">Successfully retrieved role</response>
        /// <response code="401">Authentication required</response>
        /// <response code="404">Role not found</response>
        [HttpGet("role")]
        [ProtectedEndPoint]
        public async Task<GetRoleResponse> GetRole([FromQuery] GetRoleRequest query)
        {
            return await _resourceQueryService.GetRoleAsync(query.Id);
        }

        /// <summary>
        /// Assign roles to user
        /// Sets user's role membership in single operation
        /// Replaces previous role assignments
        /// </summary>
        /// <param name="command">Request with user ID and role IDs to assign</param>
        /// <returns>Role assignment result</returns>
        /// <response code="200">Roles assigned successfully</response>
        /// <response code="400">Invalid user or role IDs</response>
        /// <response code="401">Authentication required</response>
        [HttpPost("roles/assign")]
        [ProtectedEndPoint]
        public async Task<IActionResult> SetRoles([FromBody] SetRolesRequest command)
        {
            var result = await _resourceMutationService.SetRolesAsync(command);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get resource groups
        /// Retrieves all resource groups for permission organization
        /// Shows grouped resources and their protection levels
        /// </summary>
        /// <param name="request">Optional filter parameters</param>
        /// <returns>List of resource groups with metadata</returns>
        /// <response code="200">Successfully retrieved resource groups</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("resource-groups")]
        [ProtectedEndPoint]
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
        [ProtectedEndPoint]
        public async Task<IActionResult> Create([FromBody] CreateUserRequest command)
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
        [ProtectedEndPoint]
        public async Task<IActionResult> Update([FromBody] UpdateUserRequest command)
        {
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
        [ProtectedEndPoint]
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
        [HttpPost("users")]
        [ProtectedEndPoint]
        public async Task<GetUsersResponse> GetUsers([FromBody] GetUsersRequest query)
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
        [ProtectedEndPoint]
        public async Task<GetUserResponse> GetUser([FromQuery] GetUserRequest query)
        {
            return await _userManagementQueryService.GetUserAsync(query.Id);
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
        [HttpGet("user/roles")]
        [ProtectedEndPoint]
        public async Task<GetUserRolesResponse> GetUserRoles([FromQuery] GetUserRolesRequest query)
        {
            return await _userManagementQueryService.GetUserRolesAsync(query.Id);
        }

        /// <summary>
        /// Get permissions for specific user
        /// Returns aggregated permissions from all assigned roles
        /// Shows effective permissions for the user
        /// </summary>
        /// <param name="query">Request with user ID</param>
        /// <returns>List of user's effective permissions</returns>
        /// <response code="200">Successfully retrieved user permissions</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("user/permissions")]
        [ProtectedEndPoint]
        public async Task<GetUserPermissionsResponse> GetUserPermissions([FromQuery] GetUserPermissionsRequest query)
        {
            return await _userManagementQueryService.GetUserPermissionsAsync(query.Id);
        }

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
        [ProtectedEndPoint]
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
        [ProtectedEndPoint]
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
        [HttpGet("account/roles")]
        [ProtectedEndPoint]
        public async Task<GetAccountRolesResponse> GetAccountRoles()
        {
            return await _userManagementQueryService.GetAccountRolesAsync();
        }

        /// <summary>
        /// Get authenticated user's permissions in current account
        /// Returns effective permissions from all assigned roles
        /// For access control decision-making
        /// </summary>
        /// <returns>User's effective permissions in current account</returns>
        /// <response code="200">Successfully retrieved account permissions</response>
        /// <response code="401">Authentication required</response>
        [HttpGet("account/permissions")]
        [ProtectedEndPoint]
        public async Task<GetAccountPermissionsResponse> GetAccountPermissions()
        {
            return await _userManagementQueryService.GetAccountPermissionsAsync();
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
        [HttpPost("roles-permissions")]
        [ProtectedEndPoint]
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
        [ProtectedEndPoint]
        [HttpGet("user/timelines")]
        public async Task<List<UserTimeline>> GetUserTimelines(GetUserTimeLineRequest request)
        {
            return await _userManagementQueryService.GetUserTimelinesAsync(request);
        }

        #endregion

        #region Organization

        [HttpPost("organizations")]
        [Authorize]
        public async Task<BaseResponse> SaveOrganization([FromBody]  SaveOrganizationRequest request)
        {
            return await _resourceMutationService.SaveOrganizationAsync(request);
        }

        [HttpGet("organizations")]
        [Authorize]
        public async Task<GetOrganizationsResponse> GetOrganizations([FromQuery] GetOrganizationsRequest request)
        {
            return await _resourceMutationService.GetOrganizationsAsync(request);
        }

        [HttpGet("organization")]
        [Authorize]
        public async Task<GetOrganizationResponse> GetOrganization([FromQuery]  GetOrganizationRequest request)
        {
            return await _resourceMutationService.GetOrganizationAsync(request);
        }

        [HttpPost("organization/config")]
        [Authorize]
        public async Task<BaseResponse> SaveOrganizationConfig([FromBody] SaveOrganizationConfigRequest request)
        {
            return await _resourceMutationService.SaveganizationConfigAsync(request);
        }

        [HttpGet("organization/config")]
        [Authorize]
        public async Task<OrganizationConfig> GetOrganizationConfig([FromQuery] GetOrganizationConfigRequest request)
        {
            return await _resourceMutationService.GetOrganizationConfigAsync(request);
        }

        [HttpPost("signup-settings")]
        [ProtectedEndPoint]
        public async Task<SaveSignUpSettingResponse> SaveSignUpSetting([FromBody] SaveSignUpSettingRequest request)
        {
            return await _accountService.SaveSingUpSettingAsync(request);
        }

        [HttpGet("signup-settings")]
        public async Task<SignUpSetting> GetSignUpSetting([FromQuery] GetSignUpSettingRequest request)
        {
            return await _accountService.GetSignUpSettingAsync(request);
        }

        #endregion
        #region Cloud configuration
        [HttpPost("config")]
        [ProtectedEndPoint]
        public async Task<IActionResult> Save([FromBody] SaveIamConfigurationRequest request)
        {
            var result = await _configurationService.SaveIamConfigurationAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        [HttpGet("config")]
        [ProtectedEndPoint]
        public async Task<GetConfigurationResponse> Get([FromQuery] GetAuthenticationConfigurationRequest request)
        {
            return await _configurationService.GetIamConfigurationAsync();
        }
        #endregion
    }
}
