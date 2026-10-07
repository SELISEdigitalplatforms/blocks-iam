using Api.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace XUnitTest.ApiTests;

/// <summary>
/// The SPA's CSP. Three things went wrong with the first version of this policy and all
/// three are pinned here: the host list was hardcoded to <c>dev-*</c>, <c>script-src 'self'</c>
/// blocked the reCAPTCHA script the login and signup pages load, and <c>style-src 'self'</c>
/// blocked the inline styles the UI libraries inject at runtime (now allowed by nonce).
/// </summary>
public class ContentSecurityPolicyTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ---------- Origins follow configuration, not the code ----------

    [Fact]
    public void Build_UsesTheConfiguredEnvironmentsHosts()
    {
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = "https://iam.example.com",
            ["FrontendRuntime:BLOCKS_OS_BASE_URL"] = "https://os.example.com",
            ["FrontendRuntime:BLOCKS_DATA_BASE_URL"] = "https://data.example.com",
        }));

        policy.Should().Contain("https://iam.example.com");
        policy.Should().Contain("https://os.example.com");
        policy.Should().Contain("https://data.example.com");
    }

    [Fact]
    public void Build_NamesNoEnvironmentItWasNotConfiguredWith()
    {
        // The regression this class exists for. A hardcoded dev host list meant the policy
        // was right on dev and would have stopped the SPA reaching its own API on stg/prod.
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = "https://stg-iam.blocksdevelopers.com",
            ["FrontendRuntime:BLOCKS_OS_BASE_URL"] = "https://stg-os.blocksdevelopers.com",
        }));

        policy.Should().NotContain("dev-iam.blocksdevelopers.com");
        policy.Should().NotContain("dev-os.blocksdevelopers.com");
    }

    [Fact]
    public void Build_FormActionIsTheIdentityHostAndThePortal()
    {
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = "https://iam.example.com",
            ["FrontendRuntime:BLOCKS_OS_BASE_URL"] = "https://os.example.com",
            ["FrontendRuntime:BLOCKS_DATA_BASE_URL"] = "https://data.example.com",
        }));

        var formAction = Directive(policy, "form-action");
        formAction.Should().Contain("https://iam.example.com");
        formAction.Should().Contain("https://os.example.com");
        formAction.Should().NotContain("https://data.example.com");
    }

    // ---------- WebSockets need their own scheme ----------

    [Fact]
    public void Build_EmitsTheWebSocketFormOfTheLogicHost()
    {
        // A CSP source is scheme-sensitive: https://host does not permit wss://host, which is
        // why the hardcoded policy listed wss://dev-logic separately.
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["FrontendRuntime:BLOCKS_LOGIC_BASE_URL"] = "https://logic.example.com",
        }));

        var connect = Directive(policy, "connect-src");
        connect.Should().Contain("https://logic.example.com");
        connect.Should().Contain("wss://logic.example.com");
    }

    [Theory]
    [InlineData("https://logic.example.com/hub", "wss://logic.example.com")]
    [InlineData("wss://logic.example.com", "wss://logic.example.com")]
    [InlineData("http://logic.example.com", null)]
    [InlineData("not-a-url", null)]
    [InlineData("", null)]
    public void ToWebSocketOrigin_OnlyAllowsEncryptedSockets(string value, string? expected)
    {
        ContentSecurityPolicy.ToWebSocketOrigin(value).Should().Be(expected);
    }

    // The unencrypted socket origin, built from the scheme constant so the test reads the same
    // value the policy would have emitted before plain sockets were dropped.
    private static readonly string PlainSocketOrigin =
        new UriBuilder(Uri.UriSchemeWs, "logic.example.com").Uri.GetLeftPart(UriPartial.Authority);

    [Fact]
    public void ToWebSocketOrigin_RefusesAPlainSocketOrigin()
    {
        ContentSecurityPolicy.ToWebSocketOrigin(PlainSocketOrigin).Should().BeNull();
    }

    [Fact]
    public void Build_GivesAPlainHttpLogicHostNoSocketAllowance()
    {
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["FrontendRuntime:BLOCKS_LOGIC_BASE_URL"] = "http://logic.example.com",
        }));

        Directive(policy, "connect-src").Should().NotContain(PlainSocketOrigin);
    }

    // ---------- Third-party origins the runtime config cannot describe ----------

    [Fact]
    public void Build_AllowsTheCaptchaOriginsTheLoginPageLoads()
    {
        // reCaptcha.tsx injects https://www.google.com/recaptcha/api.js and renders the widget
        // in an iframe. script-src 'self' with default-src 'self' blocked both, which broke
        // captcha on login and signup in every environment.
        var policy = ContentSecurityPolicy.Build(Config([]));

        Directive(policy, "script-src").Should().Contain("https://www.google.com");
        Directive(policy, "script-src").Should().Contain("https://www.gstatic.com");
        Directive(policy, "frame-src").Should().Contain("https://www.google.com");
    }

    [Fact]
    public void Build_AppendsConfiguredExtraOrigins()
    {
        var policy = ContentSecurityPolicy.Build(Config(new()
        {
            ["Csp:ExtraConnectSrc"] = "https://api.rollbar.com https://code.selise.biz",
            ["Csp:ExtraImgSrc"] = "https://cdn.example.com",
        }));

        Directive(policy, "connect-src").Should().Contain("https://api.rollbar.com");
        Directive(policy, "connect-src").Should().Contain("https://code.selise.biz");
        Directive(policy, "img-src").Should().Contain("https://cdn.example.com");
    }

    // ---------- Directives that must not drift ----------

    [Fact]
    public void Build_AllowsStyleElementsOnlyWithTheNonceAndBlocksInlineScripts()
    {
        // Radix/vaul/sonner/cmdk inject <style> elements at runtime. They now carry the
        // response nonce (stamped by /csp-nonce.js), so style-src needs no 'unsafe-inline'.
        var policy = ContentSecurityPolicy.Build(Config([]));

        Directive(policy, "style-src").Should()
            .Be($" 'self' 'nonce-{ContentSecurityPolicy.StyleNoncePlaceholder}'");
        Directive(policy, "style-src").Should().NotContain("unsafe-inline");
        Directive(policy, "script-src").Should().NotContain("'unsafe-inline'");
        Directive(policy, "script-src").Should().NotContain("'unsafe-eval'");
    }

    [Fact]
    public void Build_KeepsStyleAttributesThroughStyleSrcAttrOnly()
    {
        // Components set style attributes, which cannot carry a nonce.
        var policy = ContentSecurityPolicy.Build(Config([]));

        Directive(policy, "style-src-attr").Should().Be(" 'unsafe-inline'");
        policy.Should().NotContain("style-src-elem");
    }

    [Fact]
    public void WithStyleNonce_PutsTheResponseNonceInThePolicy()
    {
        var policy = ContentSecurityPolicy.Build(Config([]));

        var perRequest = ContentSecurityPolicy.WithStyleNonce(policy, "abc123==");

        Directive(perRequest, "style-src").Should().Be(" 'self' 'nonce-abc123=='");
        perRequest.Should().NotContain(ContentSecurityPolicy.StyleNoncePlaceholder);
    }

    [Fact]
    public void RenderIndex_PutsTheSameNonceInTheShell()
    {
        const string template =
            "<head><meta name=\"csp-nonce\" nonce=\"__CSP_STYLE_NONCE__\" /></head>";

        ContentSecurityPolicy.RenderIndex(template, "abc123==")
            .Should().Be("<head><meta name=\"csp-nonce\" nonce=\"abc123==\" /></head>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithStyleNonce_AndRenderIndex_RefuseAnEmptyNonce(string? nonce)
    {
        var policy = () => ContentSecurityPolicy.WithStyleNonce("style-src 'self'", nonce!);
        var shell = () => ContentSecurityPolicy.RenderIndex("<head></head>", nonce!);

        policy.Should().Throw<ArgumentException>();
        shell.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_KeepsTheFramingAndObjectRestrictions()
    {
        var policy = ContentSecurityPolicy.Build(Config([]));

        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("object-src 'none'");
        policy.Should().Contain("base-uri 'self'");
    }

    // ---------- A malformed value cannot inject a directive ----------

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://evil.example.com; script-src *")]
    public void ToOrigin_RejectsAnythingThatIsNotAnHttpOrWebSocketOrigin(string value)
    {
        // "https://evil...; script-src *" parses as a URI whose path carries the injection,
        // so the assertion is that only the authority survives -- never the directive.
        var origin = ContentSecurityPolicy.ToOrigin(value);
        (origin == null || !origin.Contains("script-src", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void ToOrigin_DropsPathQueryAndTrailingSlash()
    {
        ContentSecurityPolicy.ToOrigin("https://iam.example.com/api/?x=1")
            .Should().Be("https://iam.example.com");
    }

    /// <summary>The sources of one directive, without its name or the trailing separator.</summary>
    private static string Directive(string policy, string name)
    {
        var parts = policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directive = parts.Single(p => p.StartsWith(name + " ", StringComparison.Ordinal)
                                          || p.Equals(name, StringComparison.Ordinal));
        return directive[name.Length..];
    }
}
