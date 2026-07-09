using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace XUnitTest.Auth.Shared
{
    public class CookieHelperTests
    {
        private static TokenResponse BuildTokenResponse(string? access = "access-token", string? refresh = "refresh-token", DateTime? expiresUtc = null, DateTime? refreshExpiresUtc = null, string? error = null, string? cookieDomain = null)
        {
            return new TokenResponse
            {
                AccessToken = access,
                RefreshToken = refresh,
                ExpiresUtc = expiresUtc ?? DateTime.UtcNow.AddHours(1),
                RefreshExpiresUtc = refreshExpiresUtc ?? DateTime.UtcNow.AddDays(7),
                Error = error,
                CookieDomain = cookieDomain
            };
        }

        [Fact]
        public void AppendCookies_ReturnsFalse_WhenError()
        {
            var response = BuildTokenResponse(error: "invalid");
            var ctx = new DefaultHttpContext();

            var result = CookieHelper.AppendCookies(response, ctx.Response, "example.com");

            result.Should().BeFalse();
        }

        [Fact]
        public void AppendCookies_ReturnsFalse_WhenAccessTokenEmpty()
        {
            var response = BuildTokenResponse(access: null);
            var ctx = new DefaultHttpContext();

            var result = CookieHelper.AppendCookies(response, ctx.Response, "example.com");

            result.Should().BeFalse();
        }

        [Fact]
        public void AppendCookies_ReturnsFalse_WhenDomainEmpty()
        {
            var response = BuildTokenResponse();
            var ctx = new DefaultHttpContext();

            var result = CookieHelper.AppendCookies(response, ctx.Response, "");

            result.Should().BeFalse();
        }

        [Fact]
        public void AppendCookies_ReturnsFalse_WhenDomainNull()
        {
            var response = BuildTokenResponse();
            var ctx = new DefaultHttpContext();

            var result = CookieHelper.AppendCookies(response, ctx.Response, null);

            result.Should().BeFalse();
        }

        [Fact]
        public void AppendCookies_WritesAccessCookie_WhenAllValid()
        {
            var response = BuildTokenResponse();
            var ctx = new DefaultHttpContext();

            var result = CookieHelper.AppendCookies(response, ctx.Response, "example.com");

            result.Should().BeTrue();
            ctx.Response.Headers.Should().ContainKey("Set-Cookie");
        }

        [Fact]
        public void AppendCookies_WritesBothCookies_WhenRefreshTokenPresent()
        {
            var response = BuildTokenResponse(refresh: "refresh-token");
            var ctx = new DefaultHttpContext();

            CookieHelper.AppendCookies(response, ctx.Response, "example.com");

            ctx.Response.Headers.Should().Contain(h => h.Key == "Set-Cookie" && h.Value.Any(v => v.Contains("example.com")));
            ctx.Response.Headers.Should().Contain(h => h.Key == "Set-Cookie" && h.Value.Any(v => v.Contains("rt_example.com")));
        }

        [Fact]
        public void AppendCookies_OnlyWritesAccessCookie_WhenRefreshTokenEmpty()
        {
            var response = BuildTokenResponse(refresh: null);
            var ctx = new DefaultHttpContext();

            CookieHelper.AppendCookies(response, ctx.Response, "example.com");

            ctx.Response.Headers["Set-Cookie"].Count.Should().Be(1);
        }

        [Fact]
        public void DeleteAccessAndRefreshTokenCookies_DeletesBothCookies()
        {
            var ctx = new DefaultHttpContext();
            var accessOptions = new CookieOptions();
            var refreshOptions = new CookieOptions();

            CookieHelper.DeleteAccessAndRefreshTokenCookies(ctx.Response, "example.com", accessOptions, refreshOptions);

            ctx.Response.Headers["Set-Cookie"].Count.Should().BeGreaterThan(0);
        }
    }
}