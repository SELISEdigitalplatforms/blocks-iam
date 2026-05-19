namespace Iam.DomainService.Resources
{
    public class SetGroupRequest
    {
        public List<string> Permissions { get; set; } = new List<string>();
        public string Slug { get; set; }
        
    }

    public class SetGroupResponse
    {
        public bool Success { get; set; }
    }
}
