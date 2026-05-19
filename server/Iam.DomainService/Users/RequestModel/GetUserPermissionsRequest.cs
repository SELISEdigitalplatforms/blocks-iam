using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUserPermissionsRequest
    {
        public string? Id { get; set; }
    }

    public class GetUserPermissionsResponse : BaseQueryListResponse<List<GetUserPermission>>
    {

    }
}
