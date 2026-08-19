namespace Iam.DomainService.Resources
{
    public class SetRolesRequest
    {
        public List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public string Slug { get; set; }
        public string? OrganizationId { get; set; }

        /// <summary>
        /// Apply this change to every organization's copy of the role, not just the caller's.
        /// </summary>
        /// <remarks>
        /// Opt-in per request and DELTA ONLY: it applies exactly the permissions added or removed
        /// here, and does not otherwise reconcile an organization that had already diverged.
        /// Defaults to false so an existing client that omits it keeps today's behavior, and it is
        /// inert when multi-organization mode is off, so a single-org tenant needs no second code
        /// path.
        /// </remarks>
        public bool PropagateToAllOrganizations { get; set; }
    }

    public class SetRolesResponse
    {
        public bool Success { get; set; }

        // Aligns this envelope with the shared response contract's IsSuccess flag.
        // Mirrors Success so existing payloads keep the Success field too.
        public bool IsSuccess
        {
            get => Success;
            set => Success = value;
        }

        public Dictionary<string, string> Errors { get; set;} = new Dictionary<string, string>();
    }
}
