using System.Text.Json;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.SocialServices;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class ExternalUserMapperTests
    {
        private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

        [Fact]
        public void GenericOidcMapper_MapsAllFields()
        {
            var mapper = new GenericOidcExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""ext-id"",
                ""email"": ""u@example.com"",
                ""name"": ""Display Name"",
                ""given_name"": ""First"",
                ""family_name"": ""Last"",
                ""picture"": ""https://pic.com/p.png"",
                ""phone_number"": ""+1234567890""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("ext-id");
            user.Email.Should().Be("u@example.com");
            user.DisplayName.Should().Be("Display Name");
            user.FirstName.Should().Be("First");
            user.LastName.Should().Be("Last");
            user.ProfileImageUrl.Should().Be("https://pic.com/p.png");
            user.PhoneNumber.Should().Be("+1234567890");
        }

        [Fact]
        public void GenericOidcMapper_HandlesMissingFields()
        {
            var mapper = new GenericOidcExternalUserMapper();
            var json = ParseJson(@"{}");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().BeEmpty();
            user.Email.Should().BeEmpty();
        }

        [Fact]
        public void GoogleMapper_MapsStandardFields()
        {
            var mapper = new GoogleExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""google-id"",
                ""email"": ""g@example.com"",
                ""name"": ""Google User"",
                ""given_name"": ""G"",
                ""family_name"": ""U"",
                ""picture"": ""https://google.com/p.png""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("google-id");
            user.Email.Should().Be("g@example.com");
            user.DisplayName.Should().Be("Google User");
            user.FirstName.Should().Be("G");
            user.LastName.Should().Be("U");
            user.ProfileImageUrl.Should().Be("https://google.com/p.png");
        }

        [Fact]
        public void GoogleMapper_ProviderKey_IsGoogle()
        {
            new GoogleExternalUserMapper().ProviderKey.Should().Be(SocialLogInTypes.Google);
        }

        [Fact]
        public void GithubMapper_MapsLoginAsDisplayName()
        {
            var mapper = new GithubExternalUserMapper();
            var json = ParseJson(@"{
                ""id"": ""gh-id"",
                ""email"": ""gh@example.com"",
                ""login"": ""gh-user"",
                ""avatar_url"": ""https://github.com/a.png""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("gh-id");
            user.Email.Should().Be("gh@example.com");
            user.DisplayName.Should().Be("gh-user");
            user.ProfileImageUrl.Should().Be("https://github.com/a.png");
        }

        [Fact]
        public void MicrosoftMapper_UsesOidOrSub()
        {
            var mapper = new MicrosoftExternalUserMapper();
            var json = ParseJson(@"{
                ""oid"": ""ms-oid"",
                ""preferred_username"": ""ms@example.com"",
                ""name"": ""Microsoft User"",
                ""given_name"": ""MS"",
                ""family_name"": ""User""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("ms-oid");
            user.Email.Should().Be("ms@example.com");
        }

        [Fact]
        public void MicrosoftMapper_FallsBackToSub_WhenOidMissing()
        {
            var mapper = new MicrosoftExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""ms-sub"",
                ""email"": ""ms@example.com"",
                ""name"": ""Microsoft User""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("ms-sub");
            user.Email.Should().Be("ms@example.com");
        }

        [Fact]
        public void LinkedInMapper_BuildsDisplayName_FromFirstAndLast()
        {
            var mapper = new LinkedInExternalUserMapper();
            var json = ParseJson(@"{
                ""id"": ""li-id"",
                ""localizedFirstName"": ""First"",
                ""localizedLastName"": ""Last"",
                ""email"": ""li@example.com"",
                ""profilePicture"": ""https://li.com/p.png""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("li-id");
            user.FirstName.Should().Be("First");
            user.LastName.Should().Be("Last");
            user.DisplayName.Should().Be("First Last");
            user.Email.Should().Be("li@example.com");
            user.ProfileImageUrl.Should().Be("https://li.com/p.png");
        }

        [Fact]
        public void FacebookMapper_MapsStandardFields()
        {
            var mapper = new FacebookExternalUserMapper();
            var json = ParseJson(@"{
                ""id"": ""fb-id"",
                ""email"": ""fb@example.com"",
                ""name"": ""FB User"",
                ""picture"": ""https://fb.com/p.png""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("fb-id");
            user.Email.Should().Be("fb@example.com");
            user.DisplayName.Should().Be("FB User");
            user.ProfileImageUrl.Should().Be("https://fb.com/p.png");
        }

        [Fact]
        public void OktaMapper_MapsStandardFields()
        {
            var mapper = new OktaExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""okta-id"",
                ""email"": ""okta@example.com"",
                ""name"": ""Okta User"",
                ""given_name"": ""First"",
                ""family_name"": ""Last""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("okta-id");
            user.Email.Should().Be("okta@example.com");
            user.DisplayName.Should().Be("Okta User");
            user.FirstName.Should().Be("First");
            user.LastName.Should().Be("Last");
        }

        [Fact]
        public void KeycloakMapper_FallsBackToPreferredUsername_ForDisplayName()
        {
            var mapper = new KeycloakExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""kc-id"",
                ""email"": ""kc@example.com"",
                ""preferred_username"": ""kc-user""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("kc-id");
            user.Email.Should().Be("kc@example.com");
            user.DisplayName.Should().Be("kc-user");
        }

        [Fact]
        public void KeycloakMapper_PrefersNameOverPreferredUsername()
        {
            var mapper = new KeycloakExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""kc-id"",
                ""email"": ""kc@example.com"",
                ""name"": ""Display Name"",
                ""preferred_username"": ""kc-user""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.DisplayName.Should().Be("Display Name");
        }

        [Fact]
        public void PingMapper_MapsStandardFields()
        {
            var mapper = new PingExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""ping-id"",
                ""email"": ""ping@example.com"",
                ""name"": ""Ping User""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("ping-id");
            user.Email.Should().Be("ping@example.com");
            user.DisplayName.Should().Be("Ping User");
        }

        [Fact]
        public void AdfsMapper_PrefersNameIdOverSub()
        {
            var mapper = new AdfsExternalUserMapper();
            var json = ParseJson(@"{
                ""nameid"": ""adfs-nameid"",
                ""sub"": ""adfs-sub"",
                ""upn"": ""user@adfs.com"",
                ""displayname"": ""ADFS User""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("adfs-nameid");
            user.Email.Should().Be("user@adfs.com");
            user.DisplayName.Should().Be("ADFS User");
        }

        [Fact]
        public void AdfsMapper_FallsBackToSub_WhenNameIdMissing()
        {
            var mapper = new AdfsExternalUserMapper();
            var json = ParseJson(@"{
                ""sub"": ""adfs-sub"",
                ""email"": ""user@adfs.com""
            }");
            var user = new BYOSsoUserData();

            mapper.Map(json, user);

            user.ExternalProviderUserId.Should().Be("adfs-sub");
        }
    }
}