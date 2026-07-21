using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class StateInfoTests
    {
        [Fact]
        public void StateInfo_CanBeCreated_WithRequiredProperties()
        {
            var stateInfo = new StateInfo
            {
                ClientId = "client-1",
                Provider = "google",
                Audience = "audience-1"
            };

            stateInfo.ClientId.Should().Be("client-1");
            stateInfo.Provider.Should().Be("google");
            stateInfo.Audience.Should().Be("audience-1");
            stateInfo.FlowType.Should().Be(SocialFlowType.Normal);
        }

        [Fact]
        public void StateInfo_DefaultFlowType_IsNormal()
        {
            var stateInfo = new StateInfo
            {
                ClientId = "c",
                Provider = "p",
                Audience = "a"
            };
            stateInfo.FlowType.Should().Be(SocialFlowType.Normal);
        }

        [Fact]
        public void StateInfo_CanSetOidcFlowType()
        {
            var stateInfo = new StateInfo
            {
                ClientId = "c",
                Provider = "p",
                Audience = "a",
                FlowType = SocialFlowType.Oidc
            };
            stateInfo.FlowType.Should().Be(SocialFlowType.Oidc);
        }

        [Fact]
        public void StateInfo_OptionalProperties_CanBeSet()
        {
            var stateInfo = new StateInfo
            {
                ClientId = "c",
                Provider = "p",
                Audience = "a",
                Code = "code-1",
                NextUrl = "https://next.com",
                State = "state-1",
                Scope = "openid profile",
                UserName = "user-1",
                Nonce = "nonce-1",
                Secret = "secret-1",
                RedirectUri = "https://redirect.com"
            };

            stateInfo.Code.Should().Be("code-1");
            stateInfo.NextUrl.Should().Be("https://next.com");
            stateInfo.State.Should().Be("state-1");
            stateInfo.Scope.Should().Be("openid profile");
            stateInfo.UserName.Should().Be("user-1");
            stateInfo.Nonce.Should().Be("nonce-1");
            stateInfo.Secret.Should().Be("secret-1");
            stateInfo.RedirectUri.Should().Be("https://redirect.com");
        }

        [Fact]
        public void StateInfo_ExtraDictionary_CanBeSet()
        {
            var stateInfo = new StateInfo
            {
                ClientId = "c",
                Provider = "p",
                Audience = "a",
                Extra = new Dictionary<string, string> { { "key", "value" } }
            };

            stateInfo.Extra.Should().ContainKey("key");
            stateInfo.Extra["key"].Should().Be("value");
        }
    }
}