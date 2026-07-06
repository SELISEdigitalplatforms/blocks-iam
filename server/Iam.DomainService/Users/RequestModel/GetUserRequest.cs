using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    public class GetUserRequest
    {
        public string? Id { get; set; }
    }

    public class GetUserResponse : BaseQueryResponse<Dictionary<string, object>>
    {
    }
}
