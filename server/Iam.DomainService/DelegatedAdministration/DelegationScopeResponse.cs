namespace Iam.DomainService.DelegatedAdministration
{
    /// <summary>
    /// What the caller may administer in its token organization, so a console can disable what the
    /// server would refuse. For the system user only <see cref="IsSystemUser"/>, <see cref="Mode"/>
    /// and <see cref="AssignableRoles"/> (the picker it always had) are filled.
    /// </summary>
    public sealed class DelegationScopeResponse
    {
        public bool IsSystemUser { get; init; }

        /// <summary><c>Audit</c> or <c>Enforce</c>.</summary>
        public string Mode { get; init; } = nameof(DelegatedAdministrationMode.Audit);

        public IReadOnlyList<string> HeldRoles { get; init; } = [];

        public IReadOnlyList<string> ManagedRoles { get; init; } = [];

        public IReadOnlyList<string> AssignableRoles { get; init; } = [];

        public IReadOnlyList<string> HeldPermissions { get; init; } = [];
    }
}
