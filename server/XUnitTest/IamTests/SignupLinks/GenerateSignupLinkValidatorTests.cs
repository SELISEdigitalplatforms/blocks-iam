using FluentAssertions;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

public class GenerateSignupLinkValidatorTests
{
    private readonly GenerateSignupLinkValidator _sut = new();

    private static GenerateSignupLinkRequest Valid(Action<GenerateSignupLinkRequest>? tweak = null)
    {
        var r = new GenerateSignupLinkRequest
        {
            ConfigurationId = "cfg1",
            Email = "user@example.com",
            FirstName = "Asif",
            LastName = "R",
            OrganizationId = "org-acme"
        };
        tweak?.Invoke(r);
        return r;
    }

    [Fact]
    public async Task AcceptsValidRequest()
    {
        var result = await _sut.ValidateAsync(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RejectsInvalidEmail()
    {
        var result = await _sut.ValidateAsync(Valid(r => r.Email = "not-an-email"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public async Task RejectsExpiresOutOfRange()
    {
        var result = await _sut.ValidateAsync(Valid(r => r.ExpiresInMinutes = 2));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExpiresInMinutes");
    }

    [Fact]
    public async Task RejectsAbsoluteForwardedTo()
    {
        var result = await _sut.ValidateAsync(Valid(r => r.ForwardedTo = "https://evil.example/"));
        result.IsValid.Should().BeFalse();
    }
}

public class QuerySignupLinksValidatorTests
{
    private readonly QuerySignupLinksValidator _sut = new();

    [Fact]
    public async Task AcceptsValidPaging()
    {
        var result = await _sut.ValidateAsync(new QuerySignupLinksRequest { Page = 0, PageSize = 20 });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RejectsZeroPageSize()
    {
        var result = await _sut.ValidateAsync(new QuerySignupLinksRequest { Page = 0, PageSize = 0 });
        result.IsValid.Should().BeFalse();
    }
}

public class RevokeSignupLinksByConfigurationValidatorTests
{
    private readonly RevokeSignupLinksByConfigurationValidator _sut = new();

    [Fact]
    public async Task RequiresConfigurationId()
    {
        var result = await _sut.ValidateAsync(new RevokeSignupLinksByConfigurationRequest());
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptsConfigurationId()
    {
        var result = await _sut.ValidateAsync(new RevokeSignupLinksByConfigurationRequest { ConfigurationId = "cfg1" });
        result.IsValid.Should().BeTrue();
    }
}
