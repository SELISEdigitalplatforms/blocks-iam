using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    public class HelperTests
    {
        private const string Template =
            "cid={{client_id}};ru={{redirect_uri}};scope={{Scope}};state={{State}};nonce={{Nonce}};user={{Username}};login={{LoginEndpointUrl}};key={{XBlocksKey}}";

        // ---------- LoadAuthorizationHtmlContent ----------

        [Fact]
        public void LoadAuthorizationHtmlContent_RendersModel_WithClientIdFromClientId()
        {
            var request = new AuthorizeRequest { Scope = "openid email", State = "state-xyz", Nonce = "n1" };
            var client = new OidcClientRegistration
            {
                ClientId = "client-123",
                RedirectUris = new List<string> { "https://app/callback", "https://app/other" },
                AllowedScopes = new List<string> { "openid", "profile", "email" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "alice", request, client);

            html.Should().Contain("cid=client-123");
            html.Should().Contain("ru=https://app/callback");
            html.Should().Contain("scope=openid email");
            html.Should().Contain("state=state-xyz");
            html.Should().Contain("nonce=n1");
            html.Should().Contain("user=alice");
            html.Should().Contain("login=https://login");
            html.Should().Contain("key=api-key");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_FallsBackToItemId_WhenClientIdBlank()
        {
            var request = new AuthorizeRequest { Scope = "openid" };
            var client = new OidcClientRegistration
            {
                ClientId = "",
                ItemId = "item-99",
                RedirectUris = new List<string> { "https://app/callback" },
                AllowedScopes = new List<string> { "openid" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "bob", request, client);

            html.Should().Contain("cid=item-99");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_EmptyScope_GrantsAllAllowedScopes()
        {
            var request = new AuthorizeRequest { Scope = null };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/callback" },
                AllowedScopes = new List<string> { "openid", "profile" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "u", request, client);

            html.Should().Contain("scope=openid profile");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_UnknownScope_YieldsEmptyScope()
        {
            var request = new AuthorizeRequest { Scope = "unknown_scope" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/callback" },
                AllowedScopes = new List<string> { "openid", "profile" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "u", request, client);

            html.Should().Contain("scope=;");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_NoRedirectUris_UsesEmptyRedirect()
        {
            var request = new AuthorizeRequest { Scope = "openid" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string>(),
                AllowedScopes = new List<string> { "openid" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "u", request, client);

            html.Should().Contain("ru=;");
        }

        [Fact]
        public void LoadAuthorizationHtmlContent_ScopeIntersection_IsCaseInsensitive()
        {
            var request = new AuthorizeRequest { Scope = "OpenId Email" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/callback" },
                AllowedScopes = new List<string> { "openid", "email" },
            };

            var html = Helper.LoadAuthorizationHtmlContent(Template, "https://login", "api-key", "u", request, client);

            // Intersection preserves the requested casing/order
            html.Should().Contain("scope=OpenId Email");
        }

        // ---------- GetAuthorizationError ----------

        [Fact]
        public void GetAuthorizationError_ReturnsInvalidClient_WhenClientNull()
        {
            var request = new AuthorizeRequest { ClientId = "c-missing", RedirectUri = "https://app/cb", Scope = "openid" };

            var url = Helper.GetAuthorizationError("https://err", request, null!);

            url.Should().StartWith("https://err?");
            url.Should().Contain("code=invalid_client");
            url.Should().Contain("title=Client not found");
        }

        [Fact]
        public void GetAuthorizationError_ReturnsInvalidRequest_OnRedirectUriMismatch()
        {
            var request = new AuthorizeRequest { ClientId = "c1", RedirectUri = "https://evil/cb", Scope = "openid" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/cb" },
                AllowedScopes = new List<string> { "openid" },
            };

            var url = Helper.GetAuthorizationError("https://err", request, client);

            url.Should().Contain("code=invalid_request");
            url.Should().Contain("title=Redirect URI mismatch");
        }

        [Fact]
        public void GetAuthorizationError_ReturnsInvalidScope_WhenScopeNotAllowed()
        {
            var request = new AuthorizeRequest { ClientId = "c1", RedirectUri = "https://app/cb", Scope = "openid admin" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/cb" },
                AllowedScopes = new List<string> { "openid" },
            };

            var url = Helper.GetAuthorizationError("https://err", request, client);

            url.Should().Contain("code=invalid_scope");
            url.Should().Contain("title=Scope mismatch");
        }

        [Fact]
        public void GetAuthorizationError_ReturnsPlainUri_WhenEverythingValid()
        {
            var request = new AuthorizeRequest { ClientId = "c1", RedirectUri = "https://app/cb", Scope = "openid" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/cb" },
                AllowedScopes = new List<string> { "openid", "email" },
            };

            var url = Helper.GetAuthorizationError("https://err", request, client);

            url.Should().Be("https://err");
        }

        [Fact]
        public void GetAuthorizationError_RedirectUriMatch_IsCaseInsensitive()
        {
            var request = new AuthorizeRequest { ClientId = "c1", RedirectUri = "HTTPS://APP/CB", Scope = "openid" };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/cb" },
                AllowedScopes = new List<string> { "openid" },
            };

            var url = Helper.GetAuthorizationError("https://err", request, client);

            url.Should().Be("https://err");
        }

        [Fact]
        public void GetAuthorizationError_EmptyScope_TreatedAsScopeMismatch()
        {
            var request = new AuthorizeRequest { ClientId = "c1", RedirectUri = "https://app/cb", Scope = null };
            var client = new OidcClientRegistration
            {
                ClientId = "c1",
                RedirectUris = new List<string> { "https://app/cb" },
                AllowedScopes = new List<string> { "openid" },
            };

            var url = Helper.GetAuthorizationError("https://err", request, client);

            url.Should().Contain("code=invalid_scope");
        }
    }
}
