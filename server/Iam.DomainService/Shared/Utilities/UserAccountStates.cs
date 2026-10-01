using Iam.DomainService.Entities;
using MongoDB.Driver;

namespace Iam.DomainService.Utilities
{
    /// <summary>
    /// The one lifecycle state a user list shows for an account. Locked out is not one of these:
    /// it is temporary and sits on top of any of them (see <see cref="UserAccountStates.LockedOut"/>).
    /// </summary>
    public enum UserAccountState
    {
        /// <summary>Set up and able to sign in.</summary>
        Active,

        /// <summary>Invited or created, but the account has not been activated.</summary>
        PendingVerification,

        /// <summary>Blocked, but can be reinstated.</summary>
        Suspended,

        /// <summary>Deactivated by an administrator.</summary>
        Deactivated
    }

    /// <summary>
    /// Derives an account's <see cref="UserAccountState"/> and builds the matching user-list filter.
    /// </summary>
    /// <remarks>
    /// Neither stored field is the state on its own. <see cref="User.Active"/> and
    /// <see cref="User.Status"/> are written together by activation and deactivation, but not
    /// everywhere: an invited user is created with <c>Active = false</c> while
    /// <c>Status</c> can already be <c>Active</c>, and documents written before
    /// <c>Status</c> existed have no such field, which reads as <c>Active</c>. So:
    /// <list type="number">
    /// <item><description><c>Disabled</c>, <c>Suspended</c> and <c>PendingVerification</c> are taken as stored.</description></item>
    /// <item><description>Otherwise (<c>Active</c> or missing): an active account is Active; an inactive
    /// one is Deactivated if it was ever verified and PendingVerification if it never was.</description></item>
    /// </list>
    /// <see cref="Resolve"/> and <see cref="BuildFilter"/> encode the same rule, one in memory and one
    /// for MongoDB, where a missing field matches neither <c>true</c> nor <c>false</c> -- hence the
    /// <c>$ne: true</c> and <c>$exists: false</c> clauses. Change them together.
    /// </remarks>
    public static class UserAccountStates
    {
        /// <summary>The filter value for locked-out accounts, whatever their lifecycle state.</summary>
        public const string LockedOut = "LockedOut";

        public static UserAccountState Resolve(bool active, UserLifecycleStatus status, bool isVerified) =>
            status switch
            {
                UserLifecycleStatus.Disabled => UserAccountState.Deactivated,
                UserLifecycleStatus.Suspended => UserAccountState.Suspended,
                UserLifecycleStatus.PendingVerification => UserAccountState.PendingVerification,
                _ => active
                    ? UserAccountState.Active
                    : isVerified ? UserAccountState.Deactivated : UserAccountState.PendingVerification
            };

        /// <summary>
        /// Users in any of <paramref name="states"/> -- the <see cref="UserAccountState"/> names plus
        /// <see cref="LockedOut"/>, case-insensitive. Unrecognised values are ignored, and
        /// <c>null</c> means no value was recognised, so the caller adds no clause.
        /// </summary>
        public static FilterDefinition<User>? BuildFilter(IEnumerable<string>? states, DateTime nowUtc)
        {
            if (states is null)
            {
                return null;
            }

            var builder = Builders<User>.Filter;
            var clauses = new List<FilterDefinition<User>>();
            foreach (var state in states.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(state, LockedOut, StringComparison.OrdinalIgnoreCase))
                {
                    // Strict >, the same check the sign-in flows make before refusing a login.
                    clauses.Add(builder.Gt(u => u.LockoutUntilUtc, nowUtc));
                }
                else if (Enum.TryParse<UserAccountState>(state, ignoreCase: true, out var accountState)
                    && Enum.IsDefined(accountState))
                {
                    clauses.Add(StateClause(builder, accountState));
                }
            }

            return clauses.Count == 0 ? null : builder.Or(clauses);
        }

        private static FilterDefinition<User> StateClause(FilterDefinitionBuilder<User> builder, UserAccountState state)
        {
            var statusActiveOrMissing = builder.Or(
                builder.Eq(u => u.Status, UserLifecycleStatus.Active),
                builder.Exists(u => u.Status, false));
            var inactive = builder.Ne(u => u.Active, true);

            return state switch
            {
                UserAccountState.Active => builder.And(builder.Eq(u => u.Active, true), statusActiveOrMissing),
                UserAccountState.Suspended => builder.Eq(u => u.Status, UserLifecycleStatus.Suspended),
                UserAccountState.PendingVerification => builder.Or(
                    builder.Eq(u => u.Status, UserLifecycleStatus.PendingVerification),
                    builder.And(inactive, statusActiveOrMissing, builder.Ne(u => u.IsVerified, true))),
                UserAccountState.Deactivated => builder.Or(
                    builder.Eq(u => u.Status, UserLifecycleStatus.Disabled),
                    builder.And(inactive, statusActiveOrMissing, builder.Eq(u => u.IsVerified, true))),
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };
        }
    }
}
