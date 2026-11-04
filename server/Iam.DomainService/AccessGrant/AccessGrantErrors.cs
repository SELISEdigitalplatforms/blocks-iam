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
    }
}
