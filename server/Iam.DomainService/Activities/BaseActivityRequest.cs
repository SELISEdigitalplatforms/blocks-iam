using Blocks.Genesis;

namespace Iam.DomainService.Activities
{
    public class BaseActivityRequest : BaseGetsRequest<BaseActivityFilter>
    {

    }

    public class BaseActivityFilter
    {
        public string UserId { get; set; }
    }
}
