using Blocks.Genesis;

namespace Authentication.DomainService.Shared.RequestModel
{
    public class GetAllClientCredentialsRequest : IProjectKey
    {
        public string? ProjectKey { get ; set ; }
    }
}
