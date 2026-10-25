namespace Iam.DomainService.AccessGrant
{
    /// <summary>
    /// What the caller may administer in its token organization, so a console can disable what the
    /// server would refuse. For the system user only <see cref="IsSystemUser"/>
    /// and <see cref="AssignableRoles"/> (the picker it always had) are filled.
    /// </summary>
    public sealed class GrantScopeResponse
    {
        public bool IsSystemUser { get; init; }

        public IReadOnlyList<string> HeldRoles { get; init; } = [];

        public IReadOnlyList<string> ManagedRoles { get; init; } = [];

        public IReadOnlyList<string> AssignableRoles { get; init; } = [];

        public IReadOnlyList<string> HeldPermissions { get; init; } = [];
    }
}
