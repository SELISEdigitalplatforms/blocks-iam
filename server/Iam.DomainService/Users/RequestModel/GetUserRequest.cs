using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUserRequest
    {
        public string? Id { get; set; }
    }

    public class GetUserResponse : BaseQueryResponse<GetUser>
    {
    }
}
