namespace Iam.DomainService.Accounts
{
    public class BaseAccountResponse
    {
        public string? ItemId { get; set; }
        public bool IsSuccess { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();
    }
}
