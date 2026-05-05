using Blocks.Genesis;
namespace Authentication.DomainService.Entities
{
    public class UserCode : BaseEntity
    {
        public string? Code { get; set; }
        public string? UserId { get; set; }
        public string? ClientId { get; set; }
        public int? CodeTtlInMinute { get; set; }
        public string? Note { get; set; }
    }
}
