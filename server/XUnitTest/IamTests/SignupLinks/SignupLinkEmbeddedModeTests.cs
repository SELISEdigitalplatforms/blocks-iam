using FluentAssertions;
using FluentValidation;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

/// <summary>
/// Embedded mode (SPEC26). The rules that matter here are the ones that keep the two modes
/// from leaking into each other: a client and redirect belong to OIDC only, an override moves
/// as a pair, and a document written before Mode existed must still read as OIDC.
/// </summary>
public class SignupLinkEmbeddedModeTests
{
    private static readonly CreateSignupLinkConfigurationValidator CreateValidator = new();
    private static readonly UpdateSignupLinkConfigurationValidator UpdateValidator = new();
    private static readonly GenerateSignupLinkValidator GenerateValidator = new();

    private static CreateSignupLinkConfigurationRequest Oidc() => new()
    {
        Name = "Partner onboarding",
        ClientId = "partner-web",
        RedirectUri = "https://partner.example.com/cb",
        CredentialMode = SignupLinkCredentialMode.Passwordless
    };

    private static CreateSignupLinkConfigurationRequest Embedded() => new()
    {
        Name = "Construct onboarding",
        Mode = SignupLinkMode.Embedded,
        CredentialMode = SignupLinkCredentialMode.Passwordless
    };

    private static string[] Errors(IValidator validator, object instance) =>
        validator.Validate(new ValidationContext<object>(instance))
            .Errors.Select(e => e.ErrorMessage).ToArray();

    // ---------- the zero value is Oidc, so old documents stay correct (C1) ----------

    [Fact]
    public void Mode_DefaultsToOidc()
    {
        // Both entities are BsonIgnoreExtraElements: an absent Mode deserialises to 0, and 0
        // must be Oidc or every configuration written before SPEC26 silently changes meaning.
        ((int)SignupLinkMode.Oidc).Should().Be(0);
        new SignupLinkConfiguration().Mode.Should().Be(SignupLinkMode.Oidc);
        new SignupLink().Mode.Should().Be(SignupLinkMode.Oidc);
    }

    // ---------- configuration shape ----------

    [Fact]
    public void Create_OidcWithoutClient_IsRejected()
    {
        var request = Oidc();
        request.ClientId = string.Empty;
        Errors(CreateValidator, request).Should().Contain("ClientId is required in OIDC mode");
    }

    [Fact]
    public void Create_OidcWithoutRedirect_IsRejected()
    {
        var request = Oidc();
        request.RedirectUri = string.Empty;
        Errors(CreateValidator, request).Should().Contain("RedirectUri is required in OIDC mode");
    }

    [Fact]
    public void Create_EmbeddedWithoutClientOrRedirect_IsAccepted()
    {
        Errors(CreateValidator, Embedded()).Should().BeEmpty();
    }

    [Fact]
    public void Create_EmbeddedCarryingAClient_IsRejected()
    {
        var request = Embedded();
        request.ClientId = "partner-web";
        Errors(CreateValidator, request).Should().Contain("ClientId must be empty in embedded mode");
    }

    [Fact]
    public void Create_EmbeddedCarryingARedirect_IsRejected()
    {
        var request = Embedded();
        request.RedirectUri = "https://partner.example.com/cb";
        Errors(CreateValidator, request).Should().Contain("RedirectUri must be empty in embedded mode");
    }

    // ---------- join url ----------

    [Theory]
    [InlineData("http://app.example.com/join")]      // not https
    [InlineData("https://app.example.com/join?x=1")] // query
    [InlineData("https://app.example.com/join#a")]   // fragment -- the code is appended as one
    [InlineData("/join")]                            // not absolute
    public void Create_MalformedJoinUrl_IsRejected(string joinUrl)
    {
        var request = Embedded();
        request.JoinUrl = joinUrl;
        Errors(CreateValidator, request)
            .Should().Contain("JoinUrl must be an absolute https URL with no query or fragment");
    }

    [Fact]
    public void Create_JoinUrlOnAnOidcConfiguration_IsRejected()
    {
        var request = Oidc();
        request.JoinUrl = "https://app.example.com/join";
        Errors(CreateValidator, request)
            .Should().Contain("JoinUrl applies only to embedded configurations");
    }

    [Fact]
    public void Create_ValidJoinUrl_IsAccepted()
    {
        var request = Embedded();
        request.JoinUrl = "https://app.example.com/join";
        Errors(CreateValidator, request).Should().BeEmpty();
    }

    [Fact]
    public void Create_EmbeddedWithoutJoinUrl_IsAccepted()
    {
        // JoinUrl is optional: without it the caller composes its own link from the code.
        var request = Embedded();
        request.JoinUrl = null;
        Errors(CreateValidator, request).Should().BeEmpty();
    }

    [Fact]
    public void Update_MalformedJoinUrl_IsRejected()
    {
        var request = new UpdateSignupLinkConfigurationRequest { JoinUrl = "http://app.example.com" };
        Errors(UpdateValidator, request)
            .Should().Contain("JoinUrl must be an absolute https URL with no query or fragment");
    }

    // ---------- generation: the override moves as a pair ----------

    private static GenerateSignupLinkRequest Generate() => new()
    {
        ConfigurationId = "cfg-1",
        Email = "ada@example.com",
        FirstName = "Ada",
        LastName = "Lovelace"
    };

    [Fact]
    public void Generate_NeitherOverrideSupplied_IsAccepted()
    {
        Errors(GenerateValidator, Generate()).Should().BeEmpty();
    }

    [Fact]
    public void Generate_BothOverridesSupplied_IsAccepted()
    {
        var request = Generate();
        request.ClientId = "partner-web";
        request.RedirectUri = "https://partner.example.com/cb";
        Errors(GenerateValidator, request).Should().BeEmpty();
    }

    [Fact]
    public void Generate_OnlyRedirectSupplied_IsRejected()
    {
        // A redirect URI is meaningful only against a client. Resolving them independently
        // would check a payload redirect against a configuration client it never belonged to.
        var request = Generate();
        request.RedirectUri = "https://partner.example.com/cb";
        Errors(GenerateValidator, request)
            .Should().Contain("Supply clientId and redirectUri together, or neither");
    }

    [Fact]
    public void Generate_OnlyClientSupplied_IsRejected()
    {
        var request = Generate();
        request.ClientId = "partner-web";
        Errors(GenerateValidator, request)
            .Should().Contain("Supply clientId and redirectUri together, or neither");
    }
}
