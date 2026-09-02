using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// Sparse update of one user's profile. Every field is optional and independent:
    /// <list type="bullet">
    /// <item>absent, or an explicit <c>null</c> — leave the stored value unchanged</item>
    /// <item><c>""</c> / <c>[]</c> / <c>{}</c> — clear the stored value</item>
    /// <item>a value — set it</item>
    /// </list>
    /// <para>
    /// Nullability is what carries that contract. A non-nullable property, or one with an
    /// initializer, binds an absent JSON field to a default the service cannot tell apart from a
    /// deliberate value — which is how omitting <c>roles</c> used to strip them and omitting
    /// <c>mfaEnabled</c> used to disable MFA. Nothing on this model may gain an initializer.
    /// </para>
    /// </summary>
    public class UpdateUserRequest
    {
        /// <summary>Set from the route by the controller; any value in the body is overwritten.</summary>
        public string ItemId { get; set; }

        /// <summary>Which organization's view of the user is being edited. Scope only - it never adds membership.</summary>
        public string? OrganizationId { get; set; }

        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Language { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public List<string>? Tags { get; set; }

        /// <summary>
        /// Free-form extras. Replaced wholesale when present - there is no per-key merge, so a
        /// client sends the complete bag it wants persisted.
        /// </summary>
        public Dictionary<string, object>? Attributes { get; set; }

        /// <summary>
        /// Everything the body carried that this model does not define. Roles, permissions and MFA
        /// state moved to their own endpoints and are silently skipped by the binder; capturing
        /// them here is what lets the service warn a caller that is still sending them instead of
        /// returning 200 and quietly doing nothing.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
    }
}
