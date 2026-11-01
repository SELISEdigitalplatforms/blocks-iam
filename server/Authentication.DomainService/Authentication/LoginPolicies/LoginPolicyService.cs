using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Resources;
using Authentication.DomainService.Services;

namespace Authentication.DomainService.Authentication.LoginPolicies
{
    public interface ILoginPolicyService
    {
        /// <summary>Whether this sign-in may go ahead for the current tenant.</summary>
        Task<LoginPolicyDecision> CheckAsync(
            LoginChannel channel,
            string method,
            OidcClientRegistration? client = null,
            IdentityProvider? provider = null);

        /// <summary>What a login page may offer for the current tenant (and OIDC client, when given).</summary>
        Task<LoginOptions> GetOptionsAsync(LoginChannel channel, OidcClientRegistration? client);

        /// <summary>Whether the place (embedded or OIDC) is on for the current tenant.</summary>
        Task<LoginPolicyDecision> CheckChannelAsync(LoginChannel channel);

        /// <summary>A filter for silent sign-in: which IdP session accounts may sign in to this client.</summary>
        Task<Func<IdpSessionAccount, bool>> SessionAccountFilterAsync(OidcClientRegistration? client);
    }

    /// <summary>
    /// Reads the current tenant's policy and applies <see cref="LoginPolicyRules"/>. The root tenant is
    /// never limited: it is the admin console's own sign-in, which must always work.
    /// </summary>
    public sealed class LoginPolicyService : ILoginPolicyService
    {
        private readonly IResourceRepository _resourceRepository;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ITenants _tenants;

        public LoginPolicyService(
            IResourceRepository resourceRepository,
            IAuthenticationRepository authenticationRepository,
            ITenants tenants)
        {
            _resourceRepository = resourceRepository;
            _authenticationRepository = authenticationRepository;
            _tenants = tenants;
        }

        public async Task<LoginPolicyDecision> CheckAsync(
            LoginChannel channel,
            string method,
            OidcClientRegistration? client = null,
            IdentityProvider? provider = null)
        {
            if (IsRootTenant())
                return LoginPolicyDecision.Allowed;

            var tenantPolicy = (await _resourceRepository.GetTenantConfigurationAsync())?.LoginPolicy;
            return LoginPolicyRules.Check(tenantPolicy, channel, method, client?.LoginPolicy, provider);
        }

        public async Task<LoginOptions> GetOptionsAsync(LoginChannel channel, OidcClientRegistration? client)
        {
            var socialProviders = (await _authenticationRepository.GetIdentityProvidersAsync())
                .Where(p => p.IsActive && p.ProviderType == "social")
                .ToList();

            if (IsRootTenant())
                return new LoginOptions(true, socialProviders.Count > 0, socialProviders);

            var tenantPolicy = (await _resourceRepository.GetTenantConfigurationAsync())?.LoginPolicy;
            return LoginPolicyRules.Options(tenantPolicy, channel, client?.LoginPolicy, socialProviders);
        }

        public async Task<LoginPolicyDecision> CheckChannelAsync(LoginChannel channel)
        {
            if (IsRootTenant())
                return LoginPolicyDecision.Allowed;

            var tenantPolicy = (await _resourceRepository.GetTenantConfigurationAsync())?.LoginPolicy;
            return LoginPolicyRules.CheckChannel(tenantPolicy, channel);
        }

        public async Task<Func<IdpSessionAccount, bool>> SessionAccountFilterAsync(OidcClientRegistration? client)
        {
            if (IsRootTenant())
                return _ => true;

            var tenantPolicy = (await _resourceRepository.GetTenantConfigurationAsync())?.LoginPolicy;
            var clientPolicy = client?.LoginPolicy;
            return account => LoginPolicyRules.AllowsSessionAccount(tenantPolicy, clientPolicy, account.LoginMethod);
        }

        private bool IsRootTenant()
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            return !string.IsNullOrWhiteSpace(tenantId) && _tenants.GetTenantByID(tenantId)?.IsRootTenant == true;
        }
    }
}
