using FluentAssertions;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkSummaryValidatorTests
{
    private readonly SignupLinkSummaryValidator _sut = new();

    [Fact]
    public async Task Rejects_MissingConfigurationId()
    {
        var result = await _sut.ValidateAsync(new SignupLinkSummaryRequest());
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConfigurationId"
            && e.ErrorMessage == "ConfigurationId is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_BlankConfigurationId(string? id)
    {
        var result = await _sut.ValidateAsync(new SignupLinkSummaryRequest { ConfigurationId = id! });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Accepts_ConfigurationIdOnly()
    {
        var result = await _sut.ValidateAsync(new SignupLinkSummaryRequest { ConfigurationId = "cfg1" });
        result.IsValid.Should().BeTrue();
    }
}
