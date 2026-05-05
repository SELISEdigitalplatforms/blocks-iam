using Blocks.Genesis;
using Authentication.DomainService.Entities;

namespace Authentication.DomainService.RequestModel
{
    public class GetOIDCClientRequest : IProjectKey
    {
        public string? ProjectKey { get; set ; }
        public string? ClientId { get; set; }    
    }

    public class GetOIDCClientsRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
    }

    public class DeleteOIDCClientRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string? ItemId { get; set; }
    }

    public class GetOIDCClientsResponse : BaseResponse
    {
        public List<OIDCClientCredential> oIDCClientCredentials { get; set; } = [];
    }

    public class GetOIDCClientResponse : BaseResponse
    {
        public OIDCClientCredential? oIDCClientCredential { get; set; }
    }
}
