using Authentication.DomainService.Shared.Services;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class PasswordPolicyRegexDeriverTests
    {
        [Fact]
        public void Derive_ReadsTheTenantDefaultRule()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(
                @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$");

            policy.Should().NotBeNull();
            policy!.MinLength.Should().Be(8);
            policy.MaxLength.Should().Be(30);
            policy.RequireLowercase.Should().BeTrue();
            policy.RequireUppercase.Should().BeTrue();
            policy.RequireNumbers.Should().BeTrue();
            policy.RequireSpecialChars.Should().BeTrue();
        }

        [Fact]
        public void Derive_FollowsAnEditedLengthBound()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(
                @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{5,30}$");

            policy!.MinLength.Should().Be(5);
            policy.MaxLength.Should().Be(30);
        }

        [Fact]
        public void Derive_OmittedClassesAreNotRequired()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?=.*[A-Z])(?=.*\d).{8,64}$");

            policy!.RequireUppercase.Should().BeTrue();
            policy.RequireNumbers.Should().BeTrue();
            policy.RequireLowercase.Should().BeFalse();
            policy.RequireSpecialChars.Should().BeFalse();
        }

        [Fact]
        public void Derive_LengthOnlyRuleNeedsNoLookaheads()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^.{12,40}$");

            policy!.MinLength.Should().Be(12);
            policy.MaxLength.Should().Be(40);
            policy.RequireLowercase.Should().BeFalse();
        }

        [Fact]
        public void Derive_ExactLengthQuantifierBoundsBothEnds()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^.{16}$");

            policy!.MinLength.Should().Be(16);
            policy.MaxLength.Should().Be(16);
        }

        [Fact]
        public void Derive_CarriesTheAdminsMessageThrough()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^.{8,30}$", "Avoid your username.");

            policy!.Message.Should().Be("Avoid your username.");
        }

        [Theory]
        // Open-ended: MaxLength cannot say "unbounded".
        [InlineData(@"^(?=.*[a-z]).{8,}$")]
        // Unanchored: the quantifier would not bound the whole password.
        [InlineData(@"(?=.*[a-z])[A-Za-z\d\W_]{8,30}")]
        // A body narrower than the six fields can express -- this one bans digits outright.
        [InlineData(@"^(?=.*[a-z])[a-zA-Z]{8,30}$")]
        // A lookahead the recogniser does not know: deriving would silently drop the requirement.
        [InlineData(@"^(?=.*[a-z])(?!.*(.)\1\1)[A-Za-z\d\W_]{8,30}$")]
        // No length bound at all.
        [InlineData(@"^(?=.*[a-z])[A-Za-z\d\W_]+$")]
        [InlineData("")]
        [InlineData(null)]
        public void Derive_ReturnsNullForAnythingItCannotReadConfidently(string? pattern)
        {
            PasswordPolicyRegexDeriver.Derive(pattern).Should().BeNull();
        }

        [Fact]
        public void Derive_RejectsInvertedBounds()
        {
            PasswordPolicyRegexDeriver.Derive(@"^.{30,8}$").Should().BeNull();
        }
    }
}
