namespace Iam.DomainService.Users
{
    public class IsEmailAvailableRequest
    {
        public string Email { get; set; }
    }

    public class IsEmailAvailableResponse
    {
        public bool IsAvailable { get; set; }
    }
}
