using Authentication.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// The server half of the app-scheme rule; the SPA's <c>isAppRedirectUrl</c> mirrors it.
    /// </summary>
    public class RedirectUriSchemesTests
    {
        [Theory]
        [InlineData("com.example.app:/oauth2redirect")]
        [InlineData("com.example.app://callback")]
        [InlineData("com.klaxai.app://login/callback")]
        [InlineData("Com.Example.App:/cb")]
        [InlineData("com.example-app+v2.ios:/cb?x=1")]
        public void IsAppScheme_ReverseDomainScheme_IsTrue(string value)
        {
            RedirectUriSchemes.IsAppScheme(value).Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("myapp://cb")]
        [InlineData("myapp:/cb")]
        [InlineData("intent://x#Intent;end")]
        [InlineData("com.example.app://cb#frag")]
        [InlineData("com.example.app:/cb#")]
        [InlineData("1com.example.app://cb")]
        [InlineData("com_example.app://cb")]
        [InlineData("com.example.app")]
        [InlineData("/relative/path")]
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html,hi")]
        [InlineData("file:///etc/passwd")]
        [InlineData("vbscript:msgbox")]
        [InlineData("blob:https://example.com/uuid")]
        [InlineData("about:blank")]
        [InlineData("http://example.com/cb")]
        [InlineData("https://example.com/cb")]
        public void IsAppScheme_NotAReverseDomainAppUri_IsFalse(string? value)
        {
            RedirectUriSchemes.IsAppScheme(value).Should().BeFalse();
        }

        [Theory]
        [InlineData("http://example.com/cb", true)]
        [InlineData("https://example.com/cb#frag", true)]
        [InlineData("HTTPS://EXAMPLE.COM/cb", true)]
        [InlineData("com.example.app://cb", false)]
        [InlineData("ftp://example.com", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("example.com/cb", false)]
        [InlineData(" ", false)]
        [InlineData(null, false)]
        public void IsWebUri_MatchesTheExistingHttpRule(string? value, bool expected)
        {
            RedirectUriSchemes.IsWebUri(value).Should().Be(expected);
        }
    }
}
