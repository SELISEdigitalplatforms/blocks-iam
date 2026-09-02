using System.Text.Json;
using System.Text.Json.Serialization;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// Sparse update of the caller's own profile, behind <c>POST /api/iam/me</c>. Same field
    /// semantics as <see cref="UpdateUserRequest"/> (absent or null leaves a value alone, an empty
    /// form clears it, a value sets it).
    /// <para>
    /// It is a separate type rather than a reuse of <see cref="UpdateUserRequest"/> so that
    /// privilege separation is carried by the shape instead of a runtime check. There is no
    /// <c>ItemId</c> - the subject comes from <c>BlocksContext.UserId</c>, so a caller cannot name
    /// someone else - no <c>OrganizationId</c>, since a user does not retarget another
    /// organization's view of themselves, and no <c>Tags</c>, which are an administrative
    /// classification rather than a profile field.
    /// </para>
    /// </summary>
    public class UpdateMyAccountRequest
    {
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Language { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public Dictionary<string, object>? Attributes { get; set; }

        /// <summary>
        /// Anything the body carried that this model does not define - including <c>itemId</c>,
        /// <c>organizationId</c> and <c>roles</c>, which are deliberately absent here. Captured so
        /// the service can warn rather than accept them in silence.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnmappedFields { get; set; }
    }
}
