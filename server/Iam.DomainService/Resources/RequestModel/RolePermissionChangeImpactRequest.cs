namespace Iam.DomainService.Resources
{
    /// <summary>
    /// The pending permission diff for a role, so the caller can be shown what confirming it would
    /// do before it is applied.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="SetRolesRequest"/> field for field except for the propagation flag: the
    /// preview has to answer "what happens if you tick that box", so the flag is the one thing it
    /// must not take as input. Sent as POST rather than GET because the diff is two id lists and
    /// would not survive a query string at realistic sizes.
    /// </remarks>
    public class RolePermissionChangeImpactRequest
    {
        public List<string> AddPermissions { get; set; } = new List<string>();

        public List<string> RemovePermissions { get; set; } = new List<string>();

        public string Slug { get; set; } = string.Empty;

        public string? OrganizationId { get; set; }
    }
}
