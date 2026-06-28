using Blocks.Genesis;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class GetMyOrganizationsResponse : BaseResponse
    {
        public List<MyOrganizationInfo> Organizations { get; set; } = [];
    }

    public class MyOrganizationInfo
    {
        public string ItemId { get; set; }
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
