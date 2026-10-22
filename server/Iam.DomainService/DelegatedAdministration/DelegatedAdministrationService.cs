using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Iam.DomainService.DelegatedAdministration
{
    /// <inheritdoc cref="IDelegatedAdministrationService"/>
    public sealed partial class DelegatedAdministrationService : IDelegatedAdministrationService
    {
        private readonly IDelegatedAdministrationRepository _repository;
        private readonly IOptionsMonitor<DelegatedAdministrationOptions> _options;
        private readonly ITenants _tenants;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly ILogger<DelegatedAdministrationService> _logger;
        private readonly IUserSessionTerminator? _userSessionTerminator;

        public DelegatedAdministrationService(
            IDelegatedAdministrationRepository repository,
            IOptionsMonitor<DelegatedAdministrationOptions> options,
            ITenants tenants,
            IIdentityAccessManagementService identityAccessManagementService,
            ILogger<DelegatedAdministrationService> logger,
            // Optional: registered by RegisterAllServices (Api and Worker) but not by the iam-only
            // registration. Without it R12 falls back to the logout-all queue.
            IUserSessionTerminator? userSessionTerminator = null)
        {
            _repository = repository;
            _options = options;
            _tenants = tenants;
            _identityAccessManagementService = identityAccessManagementService;
            _logger = logger;
            _userSessionTerminator = userSessionTerminator;
        }

        public DelegatedAdministrationMode Mode => _options.CurrentValue?.Mode ?? DelegatedAdministrationMode.Audit;

        public DelegationCaller CurrentCaller()
        {
            var context = BlocksContext.GetContext();
            var organizationId = string.IsNullOrWhiteSpace(context?.OrganizationId)
                ? IdpConstants.DefaultOrganizationId
                : context.OrganizationId;

            // Blank collapses to "default" here on purpose, unlike the write-scope resolvers: for
            // this feature "default" is the system user, and every caller the platform itself
            // makes -- workers, signup, SSO provisioning -- carries no organization.
            var isSystem = context is null
                || !context.IsAuthenticated
                || string.IsNullOrWhiteSpace(context.UserId)
                || string.Equals(organizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal)
                || context.Impersonated
                || IsRootTenant(context.TenantId);

            return new DelegationCaller(context?.UserId ?? string.Empty, organizationId, isSystem);
        }

        public Task<GrantScope> GetScopeAsync() => GetScopeAsync(CurrentCaller());

        public Task<GrantScope> GetScopeAsync(DelegationCaller caller)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return caller.IsSystem ? Task.FromResult(GrantScope.WithoutPower(caller)) : BuildScopeAsync(caller);
        }

        public bool Authorize(GrantScope scope, DelegationDenial? denial)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (denial is null || scope.IsSystem)
            {
                return true;
            }

            if (Mode == DelegatedAdministrationMode.Enforce)
            {
                LogDenied(_logger, denial.Rule, scope.CallerId, scope.OrganizationId, denial.Target, denial.Reason);
                return false;
            }

            LogWouldDeny(_logger, denial.Rule, scope.CallerId, scope.OrganizationId, denial.Target, denial.Reason);
            return true;
        }

        public bool AuthorizeSystemOnly(string target)
        {
            var scope = GrantScope.WithoutPower(CurrentCaller());
            return Authorize(scope, DelegatedAdministrationRules.CheckSystemOnly(scope, target));
        }

        public async Task<bool> AuthorizeCurrentCallerAsync(Func<GrantScope, DelegationDenial?> check)
        {
            ArgumentNullException.ThrowIfNull(check);
            var scope = await GetScopeAsync().ConfigureAwait(false);
            return scope.IsSystem || Authorize(scope, check(scope));
        }

        public async Task<bool> AuthorizeManageUserAsync(string userId)
        {
            var scope = await GetScopeAsync().ConfigureAwait(false);
            if (scope.IsSystem || string.IsNullOrWhiteSpace(userId))
            {
                return true;
            }

            var user = await _repository.GetUserAsync(userId).ConfigureAwait(false);
            return user is null || Authorize(scope, DelegatedAdministrationRules.CheckManageUser(scope, user));
        }

        public async Task<bool> AuthorizeGrantAsync(GrantScope scope, IEnumerable<string>? roles, IEnumerable<string>? permissions, string? rule = null)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return true;
            }

            var roleList = roles?.ToList() ?? [];
            await WarnOnPolicyPowerAsync(scope, roleList).ConfigureAwait(false);
            return Authorize(scope, DelegatedAdministrationRules.CheckGrant(scope, roleList, permissions, rule));
        }

        public async Task WarnOnPolicyPowerAsync(GrantScope scope, IEnumerable<string>? roles)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (scope.IsSystem)
            {
                return;
            }

            var unheld = (roles ?? [])
                .Where(slug => !string.IsNullOrWhiteSpace(slug) && !scope.HoldsOrManages(slug))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unheld.Count == 0)
            {
                return;
            }

            try
            {
                var policyRoles = await _repository.GetPolicyRoleSlugsAsync().ConfigureAwait(false);
                foreach (var slug in unheld.Where(policyRoles.Contains))
                {
                    LogRoleCarriesPolicyPower(_logger, slug, scope.CallerId, scope.OrganizationId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Warn-only by definition: a policy store that cannot be read must never fail a grant.
                LogPolicyReadFailed(_logger, ex, scope.OrganizationId);
            }
        }

        public async Task<IReadOnlyList<string>> GetRoleHoldersAsync(GrantScope scope, string slug)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return scope.IsSystem
                ? []
                : await _repository.GetUserIdsWithRoleAsync(slug, scope.OrganizationId).ConfigureAwait(false);
        }

        public async Task EndSessionsAfterRemovalAsync(GrantScope scope, IEnumerable<string> userIds, string reason)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(userIds);
            if (scope.IsSystem)
            {
                return;
            }

            foreach (var userId in userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                if (Mode != DelegatedAdministrationMode.Enforce)
                {
                    LogWouldEndSessions(_logger, userId, scope.CallerId, scope.OrganizationId, reason);
                    continue;
                }

                await EndSessionsAsync(userId, reason).ConfigureAwait(false);
            }
        }

        public async Task<GetAssignableRolesResponse> GetAssignableRolesAsync()
        {
            var scope = await GetScopeAsync().ConfigureAwait(false);
            return await BuildAssignableRolesAsync(scope).ConfigureAwait(false);
        }

        public async Task<DelegationScopeResponse> GetDelegationScopeAsync()
        {
            var scope = await GetScopeAsync().ConfigureAwait(false);
            var assignable = await BuildAssignableRolesAsync(scope).ConfigureAwait(false);

            return new DelegationScopeResponse
            {
                IsSystemUser = scope.IsSystem,
                Mode = Mode.ToString(),
                HeldRoles = Sorted(scope.HeldRoles),
                ManagedRoles = Sorted(scope.ManagedRoles),
                AssignableRoles = Sorted(assignable.Hierarchy.Concat(assignable.Standalone).Select(r => r.Slug)),
                HeldPermissions = Sorted(scope.HeldPermissions)
            };
        }

        private async Task<GetAssignableRolesResponse> BuildAssignableRolesAsync(GrantScope scope)
        {
            var roles = scope.IsSystem
                ? await _repository.GetRolesAsync(scope.OrganizationId).ConfigureAwait(false)
                : scope.Roles.ToList();

            var referencedAncestorSlugs = roles
                .SelectMany(x => x.AncestorRoleSlugs ?? [])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var response = new GetAssignableRolesResponse();
            foreach (var role in roles.Where(r => DelegatedAdministrationRules.CheckAssignRole(scope, r.Slug) is null))
            {
                // The split the picker has always shown: a role outside every tree is standalone.
                var isStandalone = !role.CanCreateOwn
                    && string.IsNullOrWhiteSpace(role.ParentRoleSlug)
                    && (role.AncestorRoleSlugs ?? []).Count == 0
                    && !referencedAncestorSlugs.Contains(role.Slug);

                var assignable = new AssignableRole { Slug = role.Slug, Name = role.Name };
                if (isStandalone)
                {
                    response.Standalone.Add(assignable);
                }
                else
                {
                    response.Hierarchy.Add(assignable);
                }
            }

            return response;
        }

        private async Task<GrantScope> BuildScopeAsync(DelegationCaller caller)
        {
            var callerUser = await _repository.GetUserAsync(caller.UserId).ConfigureAwait(false);
            var roles = await _repository.GetRolesAsync(caller.OrganizationId).ConfigureAwait(false);
            var permissions = await _repository.GetPermissionsAsync(caller.OrganizationId).ConfigureAwait(false);

            return GrantScope.Build(caller, callerUser, roles, permissions);
        }

        // Same order as the user-mutation path: inline first, so the sessions are dead before the
        // response goes out, and the logout-all queue only as the backup.
        private async Task EndSessionsAsync(string userId, string reason)
        {
            if (_userSessionTerminator != null)
            {
                try
                {
                    await _userSessionTerminator.EndAllSessionsAsync(userId, reason).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogEndSessionsInlineFailed(_logger, ex, userId);
                }
            }

            try
            {
                await _identityAccessManagementService
                    .SendToQueueAsync(IdpConstants.AuthenticationQueue, new LogoutAllEvent { UserId = userId })
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The removal is already saved, and a refresh re-reads the user, so a lost
                // logout-all leaves a session that loses the access at its next refresh.
                LogEndSessionsQueueFailed(_logger, ex, userId);
            }
        }

        private bool IsRootTenant(string? tenantId) =>
            !string.IsNullOrWhiteSpace(tenantId) && (_tenants.GetTenantByID(tenantId)?.IsRootTenant ?? false);

        private static List<string> Sorted(IEnumerable<string> values) =>
            values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();

        [LoggerMessage(EventId = 9101, Level = LogLevel.Warning,
            Message = "DelegatedAdministration would deny {Rule} {CallerId} {OrgId} {Target} {Reason}")]
        private static partial void LogWouldDeny(ILogger logger, string rule, string callerId, string orgId, string target, string reason);

        [LoggerMessage(EventId = 9102, Level = LogLevel.Warning,
            Message = "DelegatedAdministration denied {Rule} {CallerId} {OrgId} {Target} {Reason}")]
        private static partial void LogDenied(ILogger logger, string rule, string callerId, string orgId, string target, string reason);

        [LoggerMessage(EventId = 9103, Level = LogLevel.Warning,
            Message = "DelegatedAdministration.RoleCarriesPolicyPower {Role} {CallerId} {OrgId}: a data-access policy names this role and the caller neither holds it nor a role above it")]
        private static partial void LogRoleCarriesPolicyPower(ILogger logger, string role, string callerId, string orgId);

        [LoggerMessage(EventId = 9104, Level = LogLevel.Warning,
            Message = "DelegatedAdministration could not read data-access policies for organization {OrgId}; the policy-power warning was skipped")]
        private static partial void LogPolicyReadFailed(ILogger logger, Exception exception, string orgId);

        [LoggerMessage(EventId = 9105, Level = LogLevel.Warning,
            Message = "DelegatedAdministration would end sessions {UserId} {CallerId} {OrgId} {Reason}")]
        private static partial void LogWouldEndSessions(ILogger logger, string userId, string callerId, string orgId, string reason);

        [LoggerMessage(EventId = 9106, Level = LogLevel.Error,
            Message = "DelegatedAdministration: ending sessions inline failed for user {UserId}; queueing logout-all")]
        private static partial void LogEndSessionsInlineFailed(ILogger logger, Exception exception, string userId);

        [LoggerMessage(EventId = 9107, Level = LogLevel.Error,
            Message = "DelegatedAdministration: queueing logout-all failed for user {UserId}")]
        private static partial void LogEndSessionsQueueFailed(ILogger logger, Exception exception, string userId);
    }
}
