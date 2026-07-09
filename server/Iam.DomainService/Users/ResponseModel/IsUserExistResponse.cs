namespace Iam.DomainService.Users
{
    public class IsUserExistResponse
    {
        public bool Exists { get; set; }
        public List<string> OrganizationIds { get; set; } = new List<string>();
    }
}
