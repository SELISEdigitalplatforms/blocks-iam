using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Authentication.DomainService.OAuth;
using FluentAssertions;

namespace XUnitTest.Auth.OAuth
{
    public class SocialLogInServiceBaseTests
    {
        [Fact]
        public void ExtractRolesFromJwt_Throws_OnInvalidJwt()
        {
            var act = () => SocialLogInServiceBase.ExtractRolesFromJwt("not-a-jwt");
            act.Should().Throw<Exception>();
        }

        [Fact]
        public void ExtractRolesFromJwt_ReturnsEmpty_WhenRolesClaimMissing()
        {
            var jwt = CreateJwtWithClaims(("sub", "user-1"));
            SocialLogInServiceBase.ExtractRolesFromJwt(jwt).Should().BeEmpty();
        }

        [Fact]
        public void ExtractRolesFromJwt_ReturnsSingleRole_WhenRolesIsString()
        {
            var jwt = CreateJwtWithClaims(("roles", "admin"));
            var result = SocialLogInServiceBase.ExtractRolesFromJwt(jwt);

            result.Should().ContainSingle().Which.Should().Be("admin");
        }

        [Fact]
        public void ExtractRolesFromJwt_ReturnsMultipleRoles_WhenRolesIsJsonArray()
        {
            var jwt = CreateJwtWithClaims(("roles", "[\"admin\",\"user\",\"editor\"]"));
            var result = SocialLogInServiceBase.ExtractRolesFromJwt(jwt);

            result.Should().HaveCount(3);
            result.Should().Contain(["admin", "user", "editor"]);
        }

        [Fact]
        public void ExtractRolesFromJwt_ReturnsEmpty_WhenRolesIsEmptyArray()
        {
            var jwt = CreateJwtWithClaims(("roles", "[]"));
            SocialLogInServiceBase.ExtractRolesFromJwt(jwt).Should().BeEmpty();
        }

        private static string CreateJwtWithClaims(params (string Key, string Value)[] claims)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-key-with-sufficient-length-for-hmacsha256-algorithm"));
            var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

            var jwtClaims = claims.Select(c => new System.Security.Claims.Claim(c.Key, c.Value)).ToList();
            var token = new JwtSecurityToken(
                issuer: "test-issuer",
                audience: "test-audience",
                claims: jwtClaims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return handler.WriteToken(token);
        }
    }
}