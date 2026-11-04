using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

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

        /// <summary>Sign-in policy per client ItemId; only clients that have one. Missing = default.</summary>
        public Dictionary<string, OidcClientLoginPolicy> LoginPolicies { get; set; } = [];
    }

    public class GetOIDCClientResponse : BaseResponse
    {
        public OidcClientRegistration? oIDCClientCredential { get; set; }

        /// <summary>The client's own token lifetimes; null when it uses the project values.</summary>
        public OidcClientTokenLifetimes? TokenLifetimes { get; set; }

        /// <summary>The client's sign-in policy; null = default method and providers.</summary>
        public OidcClientLoginPolicy? LoginPolicy { get; set; }
    }
}
