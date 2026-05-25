using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Users
{
    public class GetUsersRequest : BaseGetsRequest<GetUsersFilter>
    {
    }

    public class GetUsersResponse : BaseQueryListResponse<IQueryable<GetUser>>
    {

    }

}
