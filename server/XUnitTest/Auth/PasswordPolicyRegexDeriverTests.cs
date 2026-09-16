using Authentication.DomainService.Shared.Services;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class PasswordPolicyRegexDeriverTests
    {
        private static void ShouldRequireAll(string pattern, int min, int max)
        {
            var policy = PasswordPolicyRegexDeriver.Derive(pattern);

            policy.Should().NotBeNull(because: $"'{pattern}' is decodable");
            policy!.MinLength.Should().Be(min);
            policy.MaxLength.Should().Be(max);
            policy.RequireLowercase.Should().BeTrue();
            policy.RequireUppercase.Should().BeTrue();
            policy.RequireNumbers.Should().BeTrue();
            policy.RequireSpecialChars.Should().BeTrue();
        }

        [Theory]
        // The tenant default, and the same rule with an edited lower bound.
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$", 8, 30)]
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{5,30}$", 5, 30)]
        // The same rule spelled every other way people write it.
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{8,30}$", 8, 30)]
        [InlineData(@"^(?=.*?[a-z])(?=.*?[A-Z])(?=.*?\d)(?=.*?[\W_])[A-Za-z\d\W_]{8,30}$", 8, 30)]
        [InlineData(@"^(?=.*[a-z].*)(?=.*[A-Z].*)(?=.*[0-9].*)(?=.*[\W_].*).{8,30}$", 8, 30)]
        // Lookaheads in a different order, and \A...\z anchors.
        [InlineData(@"^(?=.*[\W_])(?=.*\d)(?=.*[A-Z])(?=.*[a-z]).{8,30}$", 8, 30)]
        [InlineData(@"\A(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,30}\z", 8, 30)]
        // Unions that still describe exactly one class.
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[\d])(?=.*[\W_])[\w\W]{8,30}$", 8, 30)]
        public void Derive_ReadsTheSameRuleHoweverItIsSpelled(string pattern, int min, int max) =>
            ShouldRequireAll(pattern, min, max);

        [Fact]
        public void Derive_OmittedClassesAreNotRequired()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?=.*[A-Z])(?=.*\d).{8,64}$");

            policy!.RequireUppercase.Should().BeTrue();
            policy.RequireNumbers.Should().BeTrue();
            policy.RequireLowercase.Should().BeFalse();
            policy.RequireSpecialChars.Should().BeFalse();
        }

        [Theory]
        [InlineData(@"^.{12,40}$", 12, 40)]
        [InlineData(@"^.{16}$", 16, 16)]
        public void Derive_ReadsLengthOnlyRules(string pattern, int min, int max)
        {
            var policy = PasswordPolicyRegexDeriver.Derive(pattern);

            policy!.MinLength.Should().Be(min);
            policy.MaxLength.Should().Be(max);
            policy.RequireLowercase.Should().BeFalse();
        }

        [Theory]
        // Open-ended rules take the client's own input cap as their ceiling.
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,}$", 8)]
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).+$", 1)]
        public void Derive_TreatsAnOpenEndedRuleAsCappedByTheInputLimit(string pattern, int min) =>
            ShouldRequireAll(pattern, min, PasswordPolicyRegexDeriver.UnboundedMaxLength);

        [Fact]
        public void Derive_CarriesTheAdminsMessageThrough()
        {
            PasswordPolicyRegexDeriver.Derive(@"^.{8,30}$", "Avoid your username.")!
                .Message.Should().Be("Avoid your username.");
        }

        [Fact]
        public void Derive_BlankMessageBecomesNull()
        {
            PasswordPolicyRegexDeriver.Derive(@"^.{8,30}$", "   ")!.Message.Should().BeNull();
        }

        // ---------- rules the four flags cannot express: published as a pattern ----------

        [Theory]
        // Requirements with no structured equivalent.
        [InlineData(@"^(?=.*[a-zA-Z]).{8,30}$")]        // "a letter, either case"
        [InlineData(@"^(?=.*[!@#]).{8,30}$")]           // only some specials count
        [InlineData(@"^(?=.*\W).{8,30}$")]              // every special but underscore
        [InlineData(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\w])[\s\S]{8,30}$")]
        [InlineData(@"^(?!.*(.)\1\1).{8,30}$")]         // no character three times running
        [InlineData(@"^(?=.*[a-z])(?!.*password).{8,30}$")]
        // A body narrower than the four classes -- this one bans digits and specials outright.
        [InlineData(@"^(?=.*[a-z])[a-zA-Z]{8,30}$")]
        // Shapes the structural scan cannot read at all.
        [InlineData(@"(?=.*[a-z])[A-Za-z\d\W_]{8,30}")] // unanchored
        [InlineData(@"^(?=.*[a-z])[A-Za-z\d\W_]{8,30}")]
        [InlineData(@"^(abc|def){8,30}$")]
        [InlineData(@"^(?=.*[a-z])[A-Za-z\d\W_]$")]     // no quantifier
        [InlineData(@"^.{8,30}\$")]                      // a literal "$", not an anchor
        public void Derive_PublishesThePattern_WhenTheFlagsCannotSayTheRule(string pattern)
        {
            var policy = PasswordPolicyRegexDeriver.Derive(pattern);

            policy.Should().NotBeNull(because: $"'{pattern}' is a real rule the client must still honour");
            policy!.Pattern.Should().Be(pattern);

            // No flag is ever guessed on this path: a half-decoded rule would tick requirements
            // the server does not check.
            policy.RequireUppercase.Should().BeFalse();
            policy.RequireLowercase.Should().BeFalse();
            policy.RequireNumbers.Should().BeFalse();
            policy.RequireSpecialChars.Should().BeFalse();
        }

        [Fact]
        public void Derive_KeepsTheLengthBounds_WhenOnlyTheRequirementsAreUnreadable()
        {
            // The scan read the quantifier even though the assertion defeated it, so the user
            // still gets a real "between 8 and 30 characters" row.
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?=.*[a-zA-Z]).{8,30}$");

            policy!.MinLength.Should().Be(8);
            policy.MaxLength.Should().Be(30);
        }

        [Fact]
        public void Derive_FallsBackToTheInputCap_WhenEvenTheLengthIsUnreadable()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(abc|def){8,30}$");

            policy!.MinLength.Should().Be(1);
            policy.MaxLength.Should().Be(PasswordPolicyRegexDeriver.UnboundedMaxLength);
        }

        [Fact]
        public void Derive_DoesNotSendThePattern_WhenTheFlagsAlreadySayTheRule()
        {
            // The common case keeps the pattern off the wire entirely.
            PasswordPolicyRegexDeriver.Derive(
                @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$")!
                .Pattern.Should().BeNull();
        }

        [Fact]
        public void Derive_CarriesTheMessageOnThePatternPathToo()
        {
            PasswordPolicyRegexDeriver.Derive(@"^(?=.*[a-zA-Z]).{8,30}$", "Ask IT if unsure.")!
                .Message.Should().Be("Ask IT if unsure.");
        }

        [Fact]
        public void Derive_PublishesADoubleEscapedPatternAsOpaque()
        {
            // A pattern that survived one JSON unescape too few asserts a literal backslash. That
            // is a different rule from the one the admin meant, but it is the rule the server
            // enforces -- so it is passed on as-is rather than decoded into flags that would lie.
            var pattern = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[\\W_])[A-Za-z\\d\\W_]{5,30}$";

            PasswordPolicyRegexDeriver.Derive(pattern)!.Pattern.Should().Be(pattern);
        }

        // ---------- no usable rule at all ----------

        [Theory]
        // Catastrophic backtracking and invalid syntax are refused by the save-time screening, so
        // they are never handed to a browser. Nothing is published and the client keeps its own
        // baseline.
        [InlineData("^(a+)+$")]
        [InlineData(@"^.{30,8}$")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Derive_ReturnsNull_WhenThereIsNoRuleItCanSafelyPublish(string? pattern) =>
            PasswordPolicyRegexDeriver.Derive(pattern).Should().BeNull();

        [Fact]
        public void Derive_RefusesAnOverlongPattern() =>
            PasswordPolicyRegexDeriver.Derive("^" + new string('a', 600) + "{8,30}$").Should().BeNull();

        [Fact]
        public void Derive_RefusesAPatternTheScreeningRejects()
        {
            // A .NET-only construct: it would not compile in a browser, so it is never sent.
            PasswordPolicyRegexDeriver.Derive(@"^(?i)(?=.*[a-zA-Z]).{8,30}$").Should().BeNull();
        }

    }
}
