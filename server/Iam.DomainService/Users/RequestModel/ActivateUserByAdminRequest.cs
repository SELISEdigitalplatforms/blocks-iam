namespace Iam.DomainService.Users
{
public class ActivateUserByAdminRequest
{
    public required string UserId { get; set; }
    public required string Reason { get; set; }
}
}
