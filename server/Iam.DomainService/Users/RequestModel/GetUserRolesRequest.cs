using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUserRolesRequest
    {
        public string? Id { get; set; }
    }

    public class GetUserRolesResponse : BaseQueryListResponse<List<GetUserRole>>
    {

    }
}
