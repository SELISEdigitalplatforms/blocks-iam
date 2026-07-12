using System.Text.Json.Serialization;

namespace Iam.DomainService.Dtos
{
    /// <summary>
    /// Top-level grouping applied to user activity entries so the activity log
    /// can be filtered and reported on. Each entry belongs to exactly one
    /// category, and the categories map 1:1 to sections of the user profile UI.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UserActivityCategory
    {
        /// <summary>Account-level changes: profile updates, status changes, lifecycle events.</summary>
        Account,

        /// <summary>Authentication events: logins, logouts, MFA challenges, password changes.</summary>
        Auth,

        /// <summary>Resource access events: API calls, role/permission grants, organisation membership changes.</summary>
        Resource,

        /// <summary>Compliance and audit events: terms acceptance, admin overrides, suspicious activity.</summary>
        Audit
    }
}