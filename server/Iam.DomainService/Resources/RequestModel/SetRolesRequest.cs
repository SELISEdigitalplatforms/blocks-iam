namespace Iam.DomainService.Resources
{
    public class SetRolesRequest
    {
        public List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public string Slug { get; set; }
        public string? OragnizationId { get; set; }
    }

    public class SetRolesResponse
    {
        public bool Success { get; set; }
        public Dictionary<string, string> Errors { get; set;} = new Dictionary<string, string>();
    }
}
