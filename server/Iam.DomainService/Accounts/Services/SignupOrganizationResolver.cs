using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Implements the sign-up placement table.
    /// <list type="bullet">
    /// <item>Email with "create my own organization": <see cref="SignupOrganizationOutcome.CreateOwn"/>, whatever
    /// else is configured. The organization-creation gates in <c>CreateOrganizationAsync</c> stay the authority on
    /// whether that is allowed (multi-org off, or creation from sign-up off, refuse it exactly as before), and such a
    /// user is never also added to the auto-join target.</item>
    /// <item>Multi-org off: <see cref="SignupOrganizationOutcome.Default"/>, whatever auto-join values are stored.</item>
    /// <item>Auto-join on: <see cref="SignupOrganizationOutcome.JoinTarget"/> after checking the target, or
    /// <see cref="SignupOrganizationOutcome.TargetUnavailable"/>.</item>
    /// <item>Otherwise SSO with creation from sign-up on: <see cref="SignupOrganizationOutcome.CreatePersonal"/>.</item>
    /// <item>Anything else: <see cref="SignupOrganizationOutcome.NoOrganization"/>.</item>
    /// </list>
    /// At most one database read: the target organization by id, and only for a real target id.
    /// </summary>
    public sealed class SignupOrganizationResolver : ISignupOrganizationResolver
    {
        private readonly IResourceRepository _resourceRepository;

        public SignupOrganizationResolver(IResourceRepository resourceRepository)
        {
            _resourceRepository = resourceRepository;
        }

        public async Task<SignupOrganizationDecision> ResolveAsync(
            TenantConfiguration? tenantConfiguration,
            SignupRoute route,
            bool createOrganizationRequested = false)
        {
            if (route == SignupRoute.Email && createOrganizationRequested)
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.CreateOwn);
            }

            if (!(tenantConfiguration?.IsMultiOrgEnabled ?? false))
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.Default, IdpConstants.DefaultOrganizationId);
            }

            if (tenantConfiguration.IsSignupAutoJoinEnabled)
            {
                return await ResolveTargetAsync(tenantConfiguration.SignupAutoJoinOrganizationId);
            }

            if (route == SignupRoute.Sso && tenantConfiguration.AllowOrgCreationFromSignup)
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.CreatePersonal);
            }

            return new SignupOrganizationDecision(SignupOrganizationOutcome.NoOrganization);
        }

        /// <summary>
        /// <c>default</c> is a scope sentinel rather than a stored organization, so it is never unavailable and
        /// costs no read. A blank target (for example one stored while multi-org was off, where the save does not
        /// validate it, or a direct database edit) is treated as unavailable rather than silently falling back to
        /// another placement.
        /// </summary>
        private async Task<SignupOrganizationDecision> ResolveTargetAsync(string? targetOrganizationId)
        {
            if (string.IsNullOrWhiteSpace(targetOrganizationId))
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.TargetUnavailable);
            }

            if (string.Equals(targetOrganizationId, IdpConstants.DefaultOrganizationId, StringComparison.Ordinal))
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.JoinTarget, IdpConstants.DefaultOrganizationId);
            }

            var organization = await _resourceRepository.GetOrganizationById(targetOrganizationId);
            if (organization == null || organization.IsDisabled)
            {
                return new SignupOrganizationDecision(SignupOrganizationOutcome.TargetUnavailable);
            }

            return new SignupOrganizationDecision(SignupOrganizationOutcome.JoinTarget, targetOrganizationId);
        }
    }
}
