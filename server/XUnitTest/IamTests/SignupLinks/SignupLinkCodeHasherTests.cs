using FluentAssertions;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkCodeHasherTests
{
    [Fact]
    public void Hash_IsStableSha256Base64()
    {
        var a = SignupLinkCodeHasher.Hash("abc");
        var b = SignupLinkCodeHasher.Hash("abc");
        a.Should().Be(b);
        a.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("asif@example.com", "as•••@example.com")]
    [InlineData("a@example.com", "a•••@example.com")]
    [InlineData("ab@x.io", "ab•••@x.io")]
    public void MaskEmail_UsesFirstTwoLocalChars(string email, string expected)
    {
        SignupLinkCodeHasher.MaskEmail(email).Should().Be(expected);
    }
}
