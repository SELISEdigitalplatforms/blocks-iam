using System.Security.Cryptography;

namespace Iam.DomainService.Resources
{
    /// <summary>
    /// Organization-name availability and alternative-name generation.
    /// <para>
    /// Organization names are unique case-insensitively only for a tenant that has turned
    /// <c>IsOrgNameUniquenessEnabled</c> on (enforced in
    /// <c>ResourceMutationService.CreateOrganizationAsync</c>), so a caller that picks a taken
    /// name fails outright there and nowhere else. This resolver is the single place that answers
    /// "is this free?" and "what else could they use?", shared by the anonymous availability
    /// endpoint, the signup error path, and the SSO callback. With the tenant's check off every
    /// non-blank name is free, because the create path will accept it whatever else holds it.
    /// </para>
    /// <para>
    /// Advisory only. Nothing here reserves a name, so a candidate returned as available can be
    /// taken before the caller submits — the authoritative check stays inside
    /// <c>CreateOrganizationAsync</c>.
    /// </para>
    /// </summary>
    public class OrganizationNameResolver : IOrganizationNameResolver
    {
        // Ambiguous glyphs (I, O, 0, 1) are excluded: suggestions get read aloud and retyped.
        private const string SuffixAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int SuffixLength = 5;
        private const int MaxAttempts = 5;

        private readonly IResourceRepository _resourceRepository;

        public OrganizationNameResolver(IResourceRepository resourceRepository)
        {
            _resourceRepository = resourceRepository;
        }

        public async Task<bool> IsNameAvailableAsync(string? name)
        {
            return await IsNameAvailableAsync(name, await IsUniquenessEnforcedAsync());
        }

        /// <summary>
        /// The answer for a caller that has already resolved the tenant's enforcement setting, so
        /// the loops below read the configuration once per call rather than once per candidate.
        /// </summary>
        private async Task<bool> IsNameAvailableAsync(string? name, bool uniquenessEnforced)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // Nothing can be taken when the tenant does not enforce uniqueness: the create path
            // accepts the name regardless, so reporting it unavailable would be an answer the
            // caller cannot act on.
            if (!uniquenessEnforced)
            {
                return true;
            }

            return await _resourceRepository.GetOrganizationByNameAsync(name.Trim()) == null;
        }

        private async Task<bool> IsUniquenessEnforcedAsync()
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            return tenantConfig?.IsOrgNameUniquenessEnabled ?? false;
        }

        /// <summary>
        /// Returns the base name when it is free, otherwise the first free suffixed candidate.
        /// Empty string when nothing free was found within <see cref="MaxAttempts"/> — callers
        /// must treat that as "could not resolve" rather than as a usable name.
        /// </summary>
        public async Task<string> ResolveAvailableNameAsync(string? baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return string.Empty;
            }

            var uniquenessEnforced = await IsUniquenessEnforcedAsync();
            var normalized = baseName.Trim();

            if (await IsNameAvailableAsync(normalized, uniquenessEnforced))
            {
                return normalized;
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var candidate = $"{normalized} {GenerateSuffix()}";
                if (await IsNameAvailableAsync(candidate, uniquenessEnforced))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Up to <paramref name="count"/> free alternatives, for offering the user a one-click
        /// fix instead of only telling them the name is taken. Returns fewer (possibly none)
        /// rather than looping indefinitely, and none at all for a tenant that does not enforce
        /// uniqueness — there is no clash to offer a way out of.
        /// </summary>
        public async Task<List<string>> SuggestAvailableNamesAsync(string? baseName, int count = 2)
        {
            var suggestions = new List<string>();

            if (string.IsNullOrWhiteSpace(baseName) || count <= 0)
            {
                return suggestions;
            }

            var uniquenessEnforced = await IsUniquenessEnforcedAsync();
            if (!uniquenessEnforced)
            {
                return suggestions;
            }

            var normalized = baseName.Trim();

            for (var attempt = 0; attempt < MaxAttempts && suggestions.Count < count; attempt++)
            {
                var candidate = $"{normalized} {GenerateSuffix()}";

                if (suggestions.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (await IsNameAvailableAsync(candidate, uniquenessEnforced))
                {
                    suggestions.Add(candidate);
                }
            }

            return suggestions;
        }

        public async Task<OrganizationNameAvailability> CheckAvailabilityAsync(string? name, int suggestionCount = 2)
        {
            // A single-organization tenant can never create one, so "is this name free?" has no
            // answer worth giving — and answering it would let an anonymous caller probe which
            // organization names exist on a tenant that has no organization flows at all.
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            if (tenantConfig == null || !tenantConfig.IsMultiOrgEnabled)
            {
                return new OrganizationNameAvailability { MultiOrgEnabled = false };
            }

            // Reuses the one configuration read above rather than resolving the flag again.
            var uniquenessEnforced = tenantConfig.IsOrgNameUniquenessEnabled;
            var isAvailable = await IsNameAvailableAsync(name, uniquenessEnforced);

            return new OrganizationNameAvailability
            {
                MultiOrgEnabled = true,
                UniquenessEnforced = uniquenessEnforced,
                IsAvailable = isAvailable,
                Suggestions = isAvailable
                    ? new List<string>()
                    : await SuggestAvailableNamesAsync(name, suggestionCount)
            };
        }

        private static string GenerateSuffix()
        {
            return RandomNumberGenerator.GetString(SuffixAlphabet, SuffixLength);
        }
    }
}
