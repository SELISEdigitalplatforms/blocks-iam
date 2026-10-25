using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Accounts
{
    /// <summary>The way a brand-new user is signing up.</summary>
    public enum SignupRoute
    {
        /// <summary>Email/password sign-up through <c>POST auth/signup</c>.</summary>
        Email = 0,

        /// <summary>First SSO login of an unknown email (hosted OIDC callback or direct social grant).</summary>
        Sso = 1
    }

    /// <summary>Where a brand-new user is placed.</summary>
    public enum SignupOrganizationOutcome
    {
        /// <summary>
        /// The user lands in <c>default</c>, exactly as before auto-join existed: multi-org is off. AccountService
        /// also uses it for the unreachable <c>IsSsoSignup</c> branch of <c>POST auth/signup</c>, which keeps its
        /// original placement.
        /// </summary>
        Default = 0,

        /// <summary>The user belongs to no organization and their tokens carry <c>"no-org"</c>.</summary>
        NoOrganization = 1,

        /// <summary>Email route: the user asked to create their own organization. The creation gates decide.</summary>
        CreateOwn = 2,

        /// <summary>SSO route: a personal organization is created for the user (creation from sign-up on, auto-join off).</summary>
        CreatePersonal = 3,

        /// <summary>The user joins the tenant's configured auto-join organization.</summary>
        JoinTarget = 4,

        /// <summary>Auto-join applies but its target is blank, missing or disabled. The sign-up must fail.</summary>
        TargetUnavailable = 5
    }

    /// <summary>
    /// The resolver's answer. <see cref="OrganizationId"/> is set for <see cref="SignupOrganizationOutcome.Default"/>
    /// (<c>"default"</c>) and <see cref="SignupOrganizationOutcome.JoinTarget"/> (the target id), and null otherwise.
    /// </summary>
    public sealed record SignupOrganizationDecision(SignupOrganizationOutcome Outcome, string? OrganizationId = null);

    /// <summary>
    /// The single place that decides which organization a new sign-up belongs to. Shared by the email
    /// sign-up (<c>AccountService.SignupAccountAsync</c>) and SSO first-login provisioning
    /// (<c>SsoUserProvisioningService</c>) so the two routes cannot drift apart.
    /// <para>
    /// It never decides anything for an existing or pending user, signup-link redemption, admin or gRPC
    /// creation, and it plays no part in which organization a token carries afterwards.
    /// </para>
    /// </summary>
    public interface ISignupOrganizationResolver
    {
        /// <summary>
        /// Decides the placement of a new sign-up.
        /// </summary>
        /// <param name="tenantConfiguration">The tenant's configuration, read once by the caller for this sign-up.</param>
        /// <param name="route">Email or SSO.</param>
        /// <param name="createOrganizationRequested">Email route only: the user asked to create their own organization.</param>
        Task<SignupOrganizationDecision> ResolveAsync(
            IamTenantConfiguration? tenantConfiguration,
            SignupRoute route,
            bool createOrganizationRequested = false);
    }
}
