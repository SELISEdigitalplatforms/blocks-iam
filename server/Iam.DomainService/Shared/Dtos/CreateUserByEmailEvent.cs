namespace Iam.DomainService.Dtos
{
    public class CreateUserByEmailEvent
    {
        public string Email { get; set; }
        public string EventQueue { get; set; }
        public string EventType { get; set; }
        public string OrganizationId { get; set; }
        public List<string> Roles { get; set; }
        public List<string> Permissions { get; set; }
    }
}
