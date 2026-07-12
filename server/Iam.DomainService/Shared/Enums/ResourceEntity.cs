namespace Iam.DomainService.Enums
{
    /// <summary>
    /// Kind of identity entity a permission or role is bound to. Used by the
    /// permission engine to decide whether a grant should be resolved against
    /// the user's direct permissions, their group memberships, or their role
    /// assignments.
    /// </summary>
    public enum ResourceEntity
    {
        /// <summary>No entity assigned (default for unconfigured rows).</summary>
        None,

        /// <summary>Grant is attached to a single permission key (e.g. <c>iam.user.read</c>).</summary>
        Permission,

        /// <summary>Grant is attached to a group; all members of the group inherit the permission.</summary>
        Group,

        /// <summary>Grant is attached to a role; users with the role inherit the permission.</summary>
        Role
    }
}