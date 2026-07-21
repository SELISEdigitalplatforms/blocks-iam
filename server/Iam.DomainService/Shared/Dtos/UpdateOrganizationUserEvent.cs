namespace Iam.DomainService.Dtos
{
    public class UpdateOrganizationUserEvent
    {
        public required string UserId { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public string? OrganizationId { get; set; }
    }
}
