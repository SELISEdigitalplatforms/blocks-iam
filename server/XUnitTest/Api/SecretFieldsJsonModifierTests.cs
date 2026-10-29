using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Api.Security;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Xunit;

namespace XUnitTest.ApiTests
{
    public class SecretFieldsJsonModifierTests
    {
        // The same setup Program.cs applies to the MVC JSON options.
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(SecretFieldsJsonModifier.Apply)
        };

        private static JsonNode Write(object value) => JsonNode.Parse(JsonSerializer.Serialize(value, value.GetType(), Options))!;

        private static IdentityProvider Idp() => new()
        {
            ItemId = "idp-1", Provider = "google", ProviderType = "social", ClientId = "cid",
            ClientSecret = "idp-secret", PrivateKey = "apple-key", TokenEndpointAuthMethod = "client_secret_post"
        };

        [Fact]
        public void ClientCredential_HasNoSecretKey()
        {
            var json = Write(new ClientCredential { ItemId = "cc-1", Name = "svc", ClientSecret = "cc-secret", ClientSecretReference = "cs-ref" }).AsObject();

            json.ContainsKey("clientSecret").Should().BeFalse();
            json.ContainsKey("clientSecretReference").Should().BeFalse();
            json.ToJsonString().Should().NotContain("cs-ref");
            json["itemId"]!.GetValue<string>().Should().Be("cc-1");
        }

        [Fact]
        public void ClientCredentialList_HasNoSecretKey()
        {
            var json = Write(new List<ClientCredential> { new() { ItemId = "cc-1", ClientSecret = "cc-secret" } });

            json.ToJsonString().Should().NotContain("clientSecret").And.NotContain("cc-secret");
        }

        [Fact]
        public void OidcClientResponses_HaveNoSecretKey()
        {
            var one = Write(new GetOIDCClientResponse { IsSuccess = true, oIDCClientCredential = new OidcClientRegistration { ItemId = "c1", ClientId = "c1", ClientSecret = "oidc-secret" } });
            var many = Write(new GetOIDCClientsResponse { IsSuccess = true, oIDCClientCredentials = [new() { ItemId = "c1", ClientId = "c1", ClientSecret = "oidc-secret" }] });

            one.ToJsonString().Should().NotContain("clientSecret").And.NotContain("oidc-secret").And.Contain("\"clientId\":\"c1\"");
            many.ToJsonString().Should().NotContain("clientSecret").And.NotContain("oidc-secret");
        }

        [Fact]
        public void IdentityProvider_InTheControllerWrapper_HasNoSecretOrPrivateKey()
        {
            // The controller returns new { data = providers, isSuccess = true }.
            var json = Write(new { data = new List<IdentityProvider> { Idp() }, isSuccess = true }).ToJsonString();

            // The *Reference fields carry a secret's name, never its value, so only the exact keys are checked.
            json.Should().NotContain("\"clientSecret\"").And.NotContain("\"privateKey\"")
                .And.NotContain("idp-secret").And.NotContain("apple-key")
                .And.Contain("\"clientId\":\"cid\"");
        }

        [Fact]
        public void IdentityProvider_WithSecretReferences_OmitsAllFourSecretFields()
        {
            // #649 H12, E8: the Blocks Secrets reference ids are hidden like the secrets themselves.
            var provider = Idp();
            provider.ClientSecretReference = "cs-ref-1";
            provider.PrivateKeyReference = "pk-ref-1";

            var single = Write(provider).AsObject();
            var wrapped = Write(new { data = new List<IdentityProvider> { provider }, isSuccess = true }).ToJsonString();

            single.Select(p => p.Key).Should().NotContain(new[] { "clientSecret", "privateKey", "clientSecretReference", "privateKeyReference" });
            single["clientId"]!.GetValue<string>().Should().Be("cid");
            wrapped.Should().NotContain("cs-ref-1").And.NotContain("pk-ref-1").And.NotContain("Reference")
                .And.NotContain("idp-secret").And.NotContain("apple-key");
        }

        [Fact]
        public void CreateAndRotateResponses_StillCarryTheNewSecret()
        {
            Write(new SaveClientCredentialResponse { IsSuccess = true, ClientSecret = "once" })["clientSecret"]!.GetValue<string>().Should().Be("once");
            Write(new RotateClientCredentialSecretResponse { IsSuccess = true, ClientSecret = "once" })["clientSecret"]!.GetValue<string>().Should().Be("once");
            Write(new SaveOIDCClientResponse { IsSuccess = true, ClientSecret = "once" })["clientSecret"]!.GetValue<string>().Should().Be("once");
            Write(new RotateOidcClientSecretResponse { IsSuccess = true, ClientSecret = "once" })["clientSecret"]!.GetValue<string>().Should().Be("once");
        }

        [Fact]
        public void ReadingARequestBody_IsUnchanged()
        {
            var idp = JsonSerializer.Deserialize<IdentityProvider>(
                """{"provider":"google","providerType":"social","clientId":"cid","clientSecret":"typed","tokenEndpointAuthMethod":"none","privateKey":"pk"}""",
                Options)!;

            idp.ClientSecret.Should().Be("typed");
            idp.PrivateKey.Should().Be("pk");
        }
    }
}
