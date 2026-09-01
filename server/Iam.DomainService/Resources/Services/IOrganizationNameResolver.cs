namespace Iam.DomainService.Resources
{
    public interface IOrganizationNameResolver
    {
        /// <summary>True when no organization currently holds this name (case-insensitive).</summary>
        Task<bool> IsNameAvailableAsync(string? name);

        /// <summary>
        /// The base name when free, otherwise a free suffixed variant. Empty string when none
        /// could be found — callers must not treat that as a usable name.
        /// </summary>
        Task<string> ResolveAvailableNameAsync(string? baseName);

        /// <summary>Free alternatives to offer when the requested name is taken. May be empty.</summary>
        Task<List<string>> SuggestAvailableNamesAsync(string? baseName, int count = 2);

        /// <summary>
        /// Availability plus suggestions in one call, for the public availability endpoint.
        /// Reports <see cref="OrganizationNameAvailability.MultiOrgEnabled"/> as false when the
        /// tenant cannot have organizations at all, so the caller can refuse rather than answer a
        /// question that has no meaning. The other members on this interface stay ungated — the
        /// signup and SSO paths reach them only after their own multi-org checks.
        /// </summary>
        Task<OrganizationNameAvailability> CheckAvailabilityAsync(string? name, int suggestionCount = 2);
    }

    public class OrganizationNameAvailability
    {
        public bool MultiOrgEnabled { get; set; }
        public bool IsAvailable { get; set; }
        public List<string> Suggestions { get; set; } = new List<string>();
    }
}
