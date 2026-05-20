namespace Iam.DomainService.Resources
{
    public class CreateRoleRequest
    {
        public string Name { get; set; }
        public string? Description { get; set; }
        public string Slug { get; set; }
    }
}
