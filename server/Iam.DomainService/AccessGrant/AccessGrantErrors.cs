namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// The one error a access grant refusal returns. Generic on purpose (decision
    /// FN-36): which rule refused, and why, goes to the server log only, so a caller probing the
    /// endpoint learns nothing about roles or permissions it cannot see.
    /// </summary>
    public static class AccessGrantErrors
    {
        public const string GrantNotAllowed = "Access_Grant_Not_Allowed";

        /// <summary>A fresh error map carrying the key, with the same value so either side can be matched.</summary>
        public static Dictionary<string, string> Create() => new() { { GrantNotAllowed, GrantNotAllowed } };

        /// <summary>True when <paramref name="errors"/> is a access grant refusal (HTTP 403).</summary>
        public static bool IsGrantNotAllowed(IDictionary<string, string>? errors) =>
            errors is not null && errors.ContainsKey(GrantNotAllowed);

        /// <summary>
        /// Deactivate and activate change the whole account, in every organization, so only a
        /// default organization caller may run them. Named on purpose, unlike
        /// <see cref="GrantNotAllowed"/>: it says nothing about roles or permissions, only where the
        /// caller stands, and tells an organization admin what to use instead.
        /// </summary>
        public const string DefaultOrganizationRequired = "Default_Organization_Required";

        public const string DefaultOrganizationRequiredMessage =
            "Deactivating or activating an account changes it in every organization, so it is only allowed from the default organization. Remove the user from your organization instead (users/revoke-access).";

        /// <summary>A fresh error map for <see cref="DefaultOrganizationRequired"/>.</summary>
        public static Dictionary<string, string> CreateDefaultOrganizationRequired() =>
            new() { { DefaultOrganizationRequired, DefaultOrganizationRequiredMessage } };

        /// <summary>True when <paramref name="errors"/> should be answered with HTTP 403.</summary>
        public static bool IsForbidden(IDictionary<string, string>? errors) =>
            IsGrantNotAllowed(errors)
            || (errors is not null && errors.ContainsKey(DefaultOrganizationRequired));
    }
}
