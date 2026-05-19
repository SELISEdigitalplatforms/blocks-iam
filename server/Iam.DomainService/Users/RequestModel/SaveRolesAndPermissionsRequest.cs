namespace Iam.DomainService.Users
{
    public class SaveRolesAndPermissionsRequest
    {
        public required string UserId { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();

    }

}
