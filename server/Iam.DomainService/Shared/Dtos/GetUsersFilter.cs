namespace Iam.DomainService.Dtos
{
    public class GetUsersFilter
    {
        public string? Email { get; set; }
        public string? Name { get; set; }
        public List<string> UserIds { get; set; } = [];
        public Status? Status { get; set; }

        /// <summary>
        /// Users in ANY of these states: <c>Active</c>, <c>PendingVerification</c>,
        /// <c>Suspended</c>, <c>Deactivated</c> or <c>LockedOut</c>. Derived the same way as the
        /// list's <c>accountState</c> field -- see <see cref="Utilities.UserAccountStates"/>.
        /// Combined with the other filters by AND; <see cref="Status"/> is unchanged.
        /// </summary>
        public List<string> AccountStates { get; set; } = [];
        public MFA? Mfa { get; set; }
        public DateTime? JoinedOn { get; set; }
        public DateTime? LastLogin { get; set; }
        public List<string> OrganizationIds { get; set; } = [];
        public List<string> Roles { get; set; } = [];
    }

    public class Status
    {
        public bool Active { get; set; }
        public bool Inactive { get; set; }
    }

    public class MFA
    {
        public bool Enabled { get; set; }
        public bool Disabled { get; set; }
    }
}
