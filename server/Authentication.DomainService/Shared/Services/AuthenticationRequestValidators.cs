using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using FluentValidation;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// The request validators <see cref="AuthenticationDomainService"/> uses, as one dependency.
    /// Replaces 4 separate deps (S107).
    /// </summary>
    public sealed class AuthenticationRequestValidators(
        IValidator<SaveOIDCClientRequest> oidcClient,
        IValidator<SaveOidcUiTemplateRequest> oidcUiTemplate,
        IValidator<SaveIdentityProviderRequest> saveIdentityProvider,
        IValidator<UpdateIdentityProviderRequest> updateIdentityProvider)
    {
        public IValidator<SaveOIDCClientRequest> OidcClient { get; } = oidcClient;
        public IValidator<SaveOidcUiTemplateRequest> OidcUiTemplate { get; } = oidcUiTemplate;
        public IValidator<SaveIdentityProviderRequest> SaveIdentityProvider { get; } = saveIdentityProvider;
        public IValidator<UpdateIdentityProviderRequest> UpdateIdentityProvider { get; } = updateIdentityProvider;
    }
}
