namespace Iam.DomainService.Resources
{
    public class CreateOrganizationRequest
    {
        public string Name { get; set; }
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
        public CreatedFrom CreatedFrom { get; set; } = 0;
    }

    public enum CreatedFrom
    {
        Cloud = 1,
        ConstructSignup = 2,
        ConstructPortal = 3,
    }
}
