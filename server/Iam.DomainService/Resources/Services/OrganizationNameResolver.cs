using System.Security.Cryptography;

namespace Iam.DomainService.Resources
{
    /// <summary>
    /// Organization-name availability and alternative-name generation.
    /// <para>
    /// Organization names are unique case-insensitively (enforced in
    /// <c>ResourceMutationService.CreateOrganizationAsync</c>), so a caller that picks a taken
    /// name fails outright. This resolver is the single place that answers "is this free?" and
    /// "what else could they use?", shared by the anonymous availability endpoint, the signup
    /// error path, and the SSO callback.
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
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return await _resourceRepository.GetOrganizationByNameAsync(name.Trim()) == null;
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

            var normalized = baseName.Trim();

            if (await IsNameAvailableAsync(normalized))
            {
                return normalized;
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var candidate = $"{normalized} {GenerateSuffix()}";
                if (await IsNameAvailableAsync(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Up to <paramref name="count"/> free alternatives, for offering the user a one-click
        /// fix instead of only telling them the name is taken. Returns fewer (possibly none)
        /// rather than looping indefinitely.
        /// </summary>
        public async Task<List<string>> SuggestAvailableNamesAsync(string? baseName, int count = 2)
        {
            var suggestions = new List<string>();

            if (string.IsNullOrWhiteSpace(baseName) || count <= 0)
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

                if (await IsNameAvailableAsync(candidate))
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

            var isAvailable = await IsNameAvailableAsync(name);

            return new OrganizationNameAvailability
            {
                MultiOrgEnabled = true,
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
