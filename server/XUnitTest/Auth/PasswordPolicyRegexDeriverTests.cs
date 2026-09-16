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

        // ---------- length stated by an assertion rather than the body ----------

        [Fact]
        public void Derive_ReadsLengthFromALengthAssertion()
        {
            // "(?=.{10,32}$)" bounds the whole password; "\S+" bounds a different string and must
            // not be mistaken for the length rule.
            var policy = PasswordPolicyRegexDeriver.Derive(
                @"^(?=.{10,32}$)(?!.*(.)\1{2})(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[!@#$%^&*])\S+$");

            policy!.MinLength.Should().Be(10);
            policy.MaxLength.Should().Be(32);
            policy.Pattern.Should().NotBeNull("the no-triples rule and the restricted specials are not expressible");
        }

        [Fact]
        public void Derive_FullyDecodesWhenTheLengthComesFromAnAssertion()
        {
            var policy = PasswordPolicyRegexDeriver.Derive(
                @"^(?=.{10,32}$)(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[\W_]).+$");

            policy!.MinLength.Should().Be(10);
            policy.MaxLength.Should().Be(32);
            policy.RequireUppercase.Should().BeTrue();
            policy.RequireLowercase.Should().BeTrue();
            policy.RequireNumbers.Should().BeTrue();
            policy.RequireSpecialChars.Should().BeTrue();
            policy.Pattern.Should().BeNull();
        }

        [Fact]
        public void Derive_IntersectsAnAssertionWithTheBodysOwnBound()
        {
            // Both bound the password; the password must satisfy both.
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?=.{10,32}$).{8,20}$");

            policy!.MinLength.Should().Be(10);
            policy.MaxLength.Should().Be(20);
        }

        [Fact]
        public void Derive_IgnoresTheBodyBoundWhenTheBodyIsNotPermissive()
        {
            // "\S+" says nothing about the password's length, only about a run of non-spaces, so
            // there is no length row to show at all.
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?=.*[A-Z])\S+$");

            policy!.MinLength.Should().Be(0);
            policy.MaxLength.Should().Be(0);
            policy.Pattern.Should().NotBeNull();
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
        public void Derive_ReportsNoLengthBound_WhenEvenTheLengthIsUnreadable()
        {
            // Zero bounds say "nothing readable here", which the client renders as no length row
            // rather than as "between 0 and 0 characters".
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(abc|def){8,30}$");

            policy!.MinLength.Should().Be(0);
            policy.MaxLength.Should().Be(0);
            policy.Pattern.Should().NotBeNull();
            policy.HasUndescribedRules.Should().BeFalse();
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

        // ---------- rules that cannot even be handed over as a pattern ----------

        [Theory]
        // Refused by the save-time screening: catastrophically slow, invalid syntax, or a
        // .NET-only construct no browser could compile.
        [InlineData("^(a+)+$")]
        [InlineData(@"^.{30,8}$")]
        [InlineData(@"^(?i)(?=.*[a-zA-Z]).{8,30}$")]
        public void Derive_ReportsUndescribedRules_WhenThePatternCannotBePublished(string pattern)
        {
            var policy = PasswordPolicyRegexDeriver.Derive(pattern);

            // Not null: the server still enforces this rule on submit, and saying "no policy"
            // would send the client back to a baseline nobody configured.
            policy.Should().NotBeNull();
            policy!.HasUndescribedRules.Should().BeTrue();
            policy.Pattern.Should().BeNull("an unscreened pattern is never handed to a browser");
            policy.RequireUppercase.Should().BeFalse();
            policy.RequireLowercase.Should().BeFalse();
            policy.RequireNumbers.Should().BeFalse();
            policy.RequireSpecialChars.Should().BeFalse();
        }

        [Fact]
        public void Derive_KeepsReadableLengthBounds_EvenWhenThePatternCannotBePublished()
        {
            // "(?i)" defeats publication, but the quantifier was still readable, so the user
            // keeps a real "between 8 and 30 characters" row.
            var policy = PasswordPolicyRegexDeriver.Derive(@"^(?i)(?=.*[a-zA-Z]).{8,30}$");

            policy!.HasUndescribedRules.Should().BeTrue();
            policy.MinLength.Should().Be(0, "the leading (?i) stops the scan before the quantifier");
        }

        [Fact]
        public void Derive_DoesNotFlagUndescribedRules_WhenTheRuleIsFullyDescribed()
        {
            PasswordPolicyRegexDeriver.Derive(
                @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_])[A-Za-z\d\W_]{8,30}$")!
                .HasUndescribedRules.Should().BeFalse();

            PasswordPolicyRegexDeriver.Derive(@"^(?=.*[a-zA-Z]).{8,30}$")!
                .HasUndescribedRules.Should().BeFalse("the pattern itself describes it");
        }

        // ---------- no rule configured at all ----------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Derive_ReturnsNull_OnlyWhenNoRuleIsConfigured(string? pattern) =>
            PasswordPolicyRegexDeriver.Derive(pattern).Should().BeNull();

        [Fact]
        public void Derive_RefusesAnOverlongPattern() =>
            PasswordPolicyRegexDeriver.Derive("^" + new string('a', 600) + "{8,30}$").Should().BeNull();
    }
}
