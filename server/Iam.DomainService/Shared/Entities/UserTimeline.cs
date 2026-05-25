namespace Iam.DomainService.Entities
{
    public class UserTimeline : BaseTimeline<User>
    {
        public required string UserId { get; set; }
        public required string OrganizationId { get; set; }
    }
}
