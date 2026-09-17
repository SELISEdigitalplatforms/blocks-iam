using Authentication.DomainService.Shared.Services;
using FluentAssertions;

namespace XUnitTest.Auth;

public class PasswordPolicyRegexValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(?:a)")]
    [InlineData("(?=a)a")]
    [InlineData("(?!b)a")]
    [InlineData("(?<=a)b")]
    [InlineData("(?<!a)b")]
    [InlineData("(?<name>a)")]
    [InlineData(@"^(?=.*\d).{10,64}$")]
    public void AcceptsCompatibleOrBlankRules(string? pattern)
        => PasswordPolicyRegexValidator.Validate(pattern).Should().BeNull();

    [Theory]
    [InlineData("(a)?(?(1)b|c)")]
    [InlineData("(?#comment)a")]
    [InlineData("(?'name'a)")]
    [InlineData("(?i)a")]
    [InlineData("(?m)a")]
    [InlineData("(?s)a")]
    [InlineData("(?x)a")]
    [InlineData("(?n)a")]
    [InlineData("(?-i)a")]
    [InlineData(@"\Aa")]
    [InlineData(@"a\Z")]
    [InlineData(@"a\z")]
    [InlineData(@"\Ga")]
    [InlineData(@"\p{L}")]
    [InlineData(@"\P{L}")]
    [InlineData("[a-z-[b]]")]
    [InlineData("(?<name>a)(?<-name>b)")]
    [InlineData("(?<name>a)(?<other-name>b)")]
    public void RejectsDotNetTokens(string pattern)
        => PasswordPolicyRegexValidator.Validate(pattern).Should().Be("PasswordStrengthCheckerRegex_Not_Javascript_Compatible");

    [Fact]
    public void LengthBoundaryAndFailureOrder()
    {
        PasswordPolicyRegexValidator.Validate(new string('a', 512)).Should().BeNull();
        PasswordPolicyRegexValidator.Validate(new string('(', 513)).Should().Be("PasswordStrengthCheckerRegex_Too_Long");
        PasswordPolicyRegexValidator.Validate("(?i)[").Should().Be("PasswordStrengthCheckerRegex_Invalid_Syntax");
    }

    [Fact]
    public void RejectsCatastrophicBacktracking()
        => PasswordPolicyRegexValidator.Validate("^(a+)+$").Should().Be("PasswordStrengthCheckerRegex_Too_Slow");
}
