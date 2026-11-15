using Authentication.DomainService.Authentication;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// <see cref="OidcStandardUserInfo"/> builds the OIDC UserInfo response: standard claims only,
    /// released per scope, and nothing IAM cannot vouch for.
    /// </summary>
    public class OidcStandardUserInfoTests
    {
        private static readonly IReadOnlySet<string> AllScopes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "openid", "profile", "email", "phone" };

        private static User FullUser() => new()
        {
            ItemId = "user-1",
            FirstName = "Jane",
            LastName = "Doe",
            UserName = "jane@x.com",
            Email = "jane@x.com",
            EmailVerifiedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PhoneNumber = "+8801700000000",
            PhoneVerifiedAtUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ProfileImageUrl = "https://cdn.example.com/u/1.png",
            Language = "en-US",
            LastUpdatedDate = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            // Blocks-only data that must never appear in UserInfo.
            Roles = new Dictionary<string, List<string>> { ["default"] = ["admin"] },
            Permissions = new Dictionary<string, List<string>> { ["default"] = ["p1"] },
            OrganizationIds = ["default"]
        };

        [Fact]
        public void Build_AllScopes_ReturnsExactlyTheStandardClaims()
        {
            var claims = OidcStandardUserInfo.Build(FullUser(), "user-1", AllScopes);

            claims.Should().BeEquivalentTo(new Dictionary<string, object>
            {
                ["sub"] = "user-1",
                ["name"] = "Jane Doe",
                ["given_name"] = "Jane",
                ["family_name"] = "Doe",
                ["preferred_username"] = "jane@x.com",
                ["picture"] = "https://cdn.example.com/u/1.png",
                ["locale"] = "en-US",
                ["updated_at"] = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                ["email"] = "jane@x.com",
                ["email_verified"] = true,
                ["phone_number"] = "+8801700000000",
                ["phone_number_verified"] = true
            });
        }

        [Fact]
        public void Build_OpenIdOnly_ReturnsSubOnly()
        {
            var claims = OidcStandardUserInfo.Build(FullUser(), "user-1", new HashSet<string> { "openid" });

            claims.Should().BeEquivalentTo(new Dictionary<string, object> { ["sub"] = "user-1" });
        }

        [Fact]
        public void Build_DefaultScopes_ReleaseProfileAndEmailButNotPhone()
        {
            var claims = OidcStandardUserInfo.Build(FullUser(), "user-1", OidcStandardUserInfo.DefaultScopes);

            claims.Should().ContainKeys("sub", "name", "preferred_username", "email");
            claims.Should().NotContainKeys("phone_number", "phone_number_verified");
        }

        [Fact]
        public void Build_UserWithoutFirstOrLastName_StillGetsPreferredUsername()
        {
            var user = FullUser();
            user.FirstName = null;
            user.LastName = " ";

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims.Should().NotContainKeys("name", "given_name", "family_name");
            claims["preferred_username"].Should().Be("jane@x.com");
        }

        [Fact]
        public void Build_OnlyFirstName_NameIsTheFirstName()
        {
            var user = FullUser();
            user.LastName = null;

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims["name"].Should().Be("Jane");
            claims["given_name"].Should().Be("Jane");
            claims.Should().NotContainKey("family_name");
        }

        [Fact]
        public void Build_UnverifiedEmailAndPhone_OmitVerifiedFlags()
        {
            var user = FullUser();
            user.EmailVerifiedAtUtc = null;
            user.PhoneVerifiedAtUtc = null;

            var claims = OidcStandardUserInfo.Build(user, "user-1", AllScopes);

            claims["email"].Should().Be("jane@x.com");
            claims["phone_number"].Should().Be("+8801700000000");
            claims.Should().NotContainKeys("email_verified", "phone_number_verified");
        }

        [Fact]
        public void Build_NoEmailOrPhone_OmitsThemAndTheirFlags()
        {
            var user = FullUser();
            user.Email = null;
            user.PhoneNumber = "";

            var claims = OidcStandardUserInfo.Build(user, "user-1", AllScopes);

            claims.Should().NotContainKeys("email", "email_verified", "phone_number", "phone_number_verified");
        }

        [Theory]
        [InlineData("/files/1.png")]
        [InlineData("ftp://cdn.example.com/1.png")]
        [InlineData("not a url")]
        [InlineData("")]
        public void Build_PictureThatIsNotAnAbsoluteHttpUrl_IsOmitted(string url)
        {
            var user = FullUser();
            user.ProfileImageUrl = url;

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims.Should().NotContainKey("picture");
        }

        [Theory]
        [InlineData("en", true)]
        [InlineData("de-DE", true)]
        [InlineData("zh-Hant-TW", true)]
        [InlineData("English", false)]
        [InlineData("en_US", false)]
        [InlineData("e", false)]
        [InlineData("en-", false)]
        [InlineData("", false)]
        public void Build_Locale_OnlyForLanguageTags(string language, bool expected)
        {
            var user = FullUser();
            user.Language = language;

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims.ContainsKey("locale").Should().Be(expected);
        }

        [Fact]
        public void Build_NeverUpdated_OmitsUpdatedAt()
        {
            var user = FullUser();
            user.LastUpdatedDate = default;

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims.Should().NotContainKey("updated_at");
        }

        [Fact]
        public void Build_UnspecifiedKindUpdateDate_IsReadAsUtc()
        {
            var user = FullUser();
            user.LastUpdatedDate = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Unspecified);

            var claims = OidcStandardUserInfo.Build(user, "user-1", OidcStandardUserInfo.DefaultScopes);

            claims["updated_at"].Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds());
        }

        [Fact]
        public void DefaultScopes_AreCaseInsensitive()
        {
            OidcStandardUserInfo.DefaultScopes.Should().Contain("PROFILE").And.Contain("Email");
        }

        [Fact]
        public void Build_MissingArguments_Throw()
        {
            var act1 = () => OidcStandardUserInfo.Build(null!, "user-1", AllScopes);
            var act2 = () => OidcStandardUserInfo.Build(FullUser(), " ", AllScopes);
            var act3 = () => OidcStandardUserInfo.Build(FullUser(), "user-1", null!);

            act1.Should().Throw<ArgumentNullException>();
            act2.Should().Throw<ArgumentException>();
            act3.Should().Throw<ArgumentNullException>();
        }
    }
}
