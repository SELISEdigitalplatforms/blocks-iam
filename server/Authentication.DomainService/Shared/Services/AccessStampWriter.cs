using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Authentication.DomainService.Services
{
    /// <inheritdoc cref="IAccessStampWriter"/>
    public sealed class AccessStampWriter : IAccessStampWriter
    {
        // The keys Genesis' AccessStamps reads. Replace these with AccessStamps.StampAsync once IAM is on
        // a Genesis version that ships it.
        public const string UserPrefix = "access_changed:";
        public const string OrganizationPrefix = "org_disabled:";
        public const string SessionPrefix = "org_switched:";

        // Margin over the longest access-token lifetime, for clock skew between services.
        private const int StampMarginMinutes = 5;

        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<AccessStampWriter> _logger;

        public AccessStampWriter(ICacheClient cacheClient, IAuthenticationRepository authenticationRepository, ILogger<AccessStampWriter> logger)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        public Task StampUserAsync(string userId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            return string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId)
                ? Task.CompletedTask
                : StampAsync($"{UserPrefix}{tenantId}:{userId}");
        }

        public Task StampOrganizationAsync(string organizationId)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            return string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(organizationId)
                ? Task.CompletedTask
                : StampAsync($"{OrganizationPrefix}{tenantId}:{organizationId}");
        }

        public Task StampSessionAsync(string sessionId) =>
            string.IsNullOrWhiteSpace(sessionId) ? Task.CompletedTask : StampAsync(SessionPrefix + sessionId);

        private async Task StampAsync(string key)
        {
            try
            {
                var lifetime = await StampLifetimeAsync();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                await _cacheClient.AddStringValueAsync(key, now, (long)lifetime.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write access stamp {Key}; the change applies at the next refresh", key);
            }
        }

        // The stamp must outlive every access token minted before it: the project's own lifetime or the
        // per-client maximum, whichever is longer.
        private async Task<TimeSpan> StampLifetimeAsync()
        {
            var projectMinutes = IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes;
            try
            {
                var configuration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
                projectMinutes = configuration?.AccessTokenValidForNumberMinutes ?? projectMinutes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the access-token lifetime; using the per-client maximum");
            }

            return TimeSpan.FromMinutes(Math.Max(projectMinutes, OidcClientTokenLifetimes.MaxAccessTokenMinutes) + StampMarginMinutes);
        }
    }
}
