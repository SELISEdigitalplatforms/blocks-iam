namespace Iam.DomainService.Resources
{
    public class GetFeResourceFeatureRequest
    {
        public string? Search { get; set; }
        public bool? IsBuiltIn { get; set; }
    }

    public class GetFeResourceFeatureResponse
    {
        public string Resource { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }
}
