namespace Iam.DomainService.Users
{
    /// <summary>
    /// Decides which keys a user response keeps.
    /// </summary>
    /// <remarks>
    /// A tenant can add fields beyond the default or cut below it, up to what the mapper builds.
    /// The mapper is the ceiling: a configured name it never emitted is ignored, so no setting can
    /// reach a field the Mongo projection never loaded or anything dropped at the GetAccounts
    /// projection -- passwords, security stamps and failure counters are unreachable by
    /// configuration. Organization scoping stays in the mappers; it is an invariant, not a setting.
    /// </remarks>
    public static class UserFieldPolicy
    {
        /// <summary>Always kept: the row key every caller needs to address the user.</summary>
        private const string RowKey = "itemId";

        /// <summary>
        /// What the list returns when nothing is configured. Status, MFA, roles and lockout are
        /// built by the mapper but left out here -- a tenant that wants them names them in
        /// UserListFields.
        /// </summary>
        public static readonly string[] DefaultListFields =
        [
            "itemId", "firstName", "lastName", "email", "userName", "active", "isVerified",
            "profileImageUrl", "lastLoggedInTime", "loginCount", "createdDate"
        ];

        /// <summary>
        /// What the detail endpoints return when nothing is configured: their full existing shape,
        /// unchanged. Names not emitted by a given mapper simply do not appear -- "organizationId"
        /// is built by GET /me and "organizationIds" by GET /users/{id}, so one list serves both.
        /// </summary>
        public static readonly string[] DefaultDetailFields =
        [
            "itemId", "createdDate", "lastUpdatedDate", "language", "salutation", "firstName",
            "lastName", "email", "phoneNumber", "roles", "permissions", "active", "status",
            "isVerified", "profileImageUrl", "mfaEnabled", "isMfaVerified", "userMfaType",
            "externalIdentities", "attributes", "logInCount", "lastLoggedInTime",
            "lastLoggedInDeviceInfo", "organizationId", "organizationIds", "lockoutUntilUtc",
            "isLockedOut", "OrganizationsRoles", "OrganizationsPermissions"
        ];

        /// <summary>
        /// The set of keys a response may keep: the configured names, or the default when nothing
        /// usable is configured. Built once per response and reused for every row.
        /// </summary>
        /// <remarks>
        /// The configuration is a hand-editable Mongo document, so blank entries are dropped and
        /// names are trimmed. A list that contains nothing usable after that is treated as absent
        /// rather than as "return almost nothing" -- an unusable configuration behaves like no
        /// configuration, which is the less surprising of the two readings.
        /// </remarks>
        public static HashSet<string> BuildKeepSet(
            IReadOnlyCollection<string>? configured,
            IReadOnlyCollection<string> fallback)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RowKey };

            if (configured is { Count: > 0 })
            {
                foreach (var name in configured)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        keep.Add(name.Trim());
                    }
                }
            }

            // Only the row key survived, so nothing usable was configured.
            if (keep.Count == 1)
            {
                foreach (var name in fallback)
                {
                    keep.Add(name);
                }
            }

            return keep;
        }

        /// <summary>
        /// Keeps the configured names, or the default when nothing is configured.
        /// </summary>
        public static Dictionary<string, object> Apply(
            Dictionary<string, object> fields,
            IReadOnlyCollection<string>? configured,
            IReadOnlyCollection<string> fallback) =>
            Apply(fields, BuildKeepSet(configured, fallback));

        /// <summary>
        /// Keeps only the keys in <paramref name="keep"/>.
        /// </summary>
        /// <remarks>
        /// An already-empty dictionary stays empty: the cross-organization guard in the mappers
        /// returns one, and re-adding the row key here would turn "you may not see this user" into
        /// "this user exists".
        /// </remarks>
        public static Dictionary<string, object> Apply(
            Dictionary<string, object> fields,
            HashSet<string> keep)
        {
            if (fields.Count == 0)
            {
                return fields;
            }

            return fields
                .Where(field => keep.Contains(field.Key))
                .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);
        }
    }
}
