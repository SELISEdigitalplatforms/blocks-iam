namespace Iam.DomainService.Resources
{
    public class SetRolesRequest
    {
        public List<string> AddPermissions { get; set; } = new List<string>();
        public List<string> RemovePermissions { get; set; } = new List<string>();
        public string Slug { get; set; }
    }

    public class SetRolesResponse
    {
        public bool Success { get; set; }
    }
}
