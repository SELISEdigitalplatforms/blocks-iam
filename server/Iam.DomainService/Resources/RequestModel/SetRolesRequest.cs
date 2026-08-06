namespace Iam.DomainService.Resources
{
    public class SetRolesRequest
    {
        public List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public string Slug { get; set; }
        public string? OrganizationId { get; set; }
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
