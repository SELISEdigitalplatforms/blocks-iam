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

        /// <summary>
        /// Null for the built-in "default" organization, which is a scope sentinel rather than a
        /// stored document and therefore has no creation date.
        /// </summary>
        public DateTime? CreatedDate { get; set; }
    }
}
