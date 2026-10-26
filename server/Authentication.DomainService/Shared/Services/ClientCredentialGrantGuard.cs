using Authentication.DomainService.Shared.RequestModel;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.AccessGrant;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// access grant for client credentials (R8). A credential is a standing grant -- a
    /// service holding its secret acts with its roles and permissions -- so it may carry only what
    /// the caller could assign to a user, and changing one is managing whoever holds it.
    /// </summary>
    public interface IClientCredentialGrantGuard
    {
        /// <summary>
        /// Create: R1 on every role and R2 on every permission requested. Update: the same, plus
        /// R3 on the roles and permissions the stored credential already carries.
        /// </summary>
        Task<bool> MaySaveAsync(SaveClientCredentialRequest request);

        /// <summary>Rotate the secret or delete: R3 on the stored credential's roles and permissions.</summary>
        Task<bool> MayChangeAsync(string? itemId);
    }

    /// <inheritdoc cref="IClientCredentialGrantGuard"/>
    /// <remarks>
    /// A credential that does not exist, or lives in another organization, passes here: the domain
    /// service answers it as not found, and this guard must not answer differently and so reveal it.
    /// </remarks>
    public sealed class ClientCredentialGrantGuard : IClientCredentialGrantGuard
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAccessGrantService _accessGrant;

        public ClientCredentialGrantGuard(
            IAuthenticationRepository authenticationRepository,
            IAccessGrantService accessGrant)
        {
            _authenticationRepository = authenticationRepository;
            _accessGrant = accessGrant;
        }

        public async Task<bool> MaySaveAsync(SaveClientCredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var scope = await _accessGrant.GetScopeAsync().ConfigureAwait(false);
            if (scope.IsSystem)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(request.ItemId)
                && !await MayChangeStoredAsync(scope, request.ItemId).ConfigureAwait(false))
            {
                return false;
            }

            return await _accessGrant
                .AuthorizeGrantAsync(scope, request.Roles, request.Permissions, AccessGrantRules.ClientCredential)
                .ConfigureAwait(false);
        }

        public async Task<bool> MayChangeAsync(string? itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return true;
            }

            var scope = await _accessGrant.GetScopeAsync().ConfigureAwait(false);
            return scope.IsSystem || await MayChangeStoredAsync(scope, itemId).ConfigureAwait(false);
        }

        private async Task<bool> MayChangeStoredAsync(GrantScope scope, string itemId)
        {
            ClientCredential? existing = await _authenticationRepository.GetClientCredentialByIdAsync(itemId).ConfigureAwait(false);
            if (existing is null || !string.Equals(existing.OrganizationId, scope.OrganizationId, StringComparison.Ordinal))
            {
                return true;
            }

            return _accessGrant.Authorize(
                scope,
                AccessGrantRules.CheckManageHolder(scope, existing.Roles, existing.Permissions, AccessGrantRules.ClientCredential, itemId));
        }
    }
}
