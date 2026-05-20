using Blocks.Genesis;

namespace Cloud.DomainService.Requests
{
    public class GetApiEndpointConfigsRequest : BaseGetsRequest<ApiEndpointConfigFilter>
    {

    }

    public class ApiEndpointConfigFilter
    {
        public string? Service { get; set; }
        public string? Method { get; set; }
        public string? Endpoint { get; set; }
    }
}
