namespace Iam.DomainService.Users
{
    public class IsUserExistResponse
    {
        public string? UserId { get; set; }
        public List<string> OrganizationIds { get; set; } = new List<string>();
    }
}
