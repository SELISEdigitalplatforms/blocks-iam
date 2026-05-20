using Blocks.Genesis;
using Authentication.DomainService.Entities;

namespace Authentication.DomainService.RequestModel
{
    public class GetOIDCClientRequest
    {
        public string? ClientId { get; set; }    
    }

    public class GetOIDCClientsRequest
    {

    }

    public class DeleteOIDCClientRequest
    {
        public string? ItemId { get; set; }
    }

    public class GetOIDCClientsResponse : BaseResponse
    {
        public List<OidcClientRegistration> oIDCClientCredentials { get; set; } = [];
    }

    public class GetOIDCClientResponse : BaseResponse
    {
        public OidcClientRegistration? oIDCClientCredential { get; set; }
    }
}
