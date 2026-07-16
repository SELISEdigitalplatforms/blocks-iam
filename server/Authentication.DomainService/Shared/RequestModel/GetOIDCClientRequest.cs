using Blocks.Genesis;
using Authentication.DomainService.Entities;

namespace Authentication.DomainService.RequestModel
{
    public sealed class GetOIDCClientRequest
    {
        public string? ClientId { get; set; }    
    }

    public sealed class GetOIDCClientsRequest
    {

    }

    public sealed class DeleteOIDCClientRequest
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
        public bool? registerAsIdentityProvider { get; set; }
    }
}
