namespace Iam.DomainService.Resources
{
    public class SaveOrganizationRequest
    {
        public string Name { get; set; }
        public bool IsEnable { get; set; }
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
    }
}
