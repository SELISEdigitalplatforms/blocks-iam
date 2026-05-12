using Blocks.Genesis;

namespace Authentication.DomainService.RequestModel
{
    public class GetSsoCredentialsRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
    }
}
