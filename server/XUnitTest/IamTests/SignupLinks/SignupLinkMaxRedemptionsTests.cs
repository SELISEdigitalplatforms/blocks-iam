using FluentAssertions;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

/// <summary>
/// The redemption budget. Two rules carry the whole feature: how a cap is resolved from the
/// payload and the configuration, and how the unlimited sentinel is read.
/// </summary>
public class SignupLinkMaxRedemptionsTests
{
    private static readonly GenerateSignupLinkValidator GenerateValidator = new();

    // ---------- resolution: payload, then configuration, then single use ----------

    [Theory]
    [InlineData(null, null, 1)]   // neither: today's behaviour is preserved
    [InlineData(null, 5, 5)]      // configuration default
    [InlineData(3, 5, 3)]         // payload wins
    [InlineData(3, null, 3)]
    [InlineData(0, 5, 0)]         // payload asks for unlimited despite a capped configuration
    [InlineData(null, 0, 0)]      // configuration is unlimited
    public void Resolve_PrefersPayloadThenConfigurationThenSingleUse(int? payload, int? config, int expected)
    {
        SignupLink.ResolveMaxRedemptions(payload, config).Should().Be(expected);
    }

    [Fact]
    public void Resolve_OmittedEverywhere_IsSingleUse()
    {
        // The default must not move. Every caller written before this feature sends no
        // maxRedemptions, and every configuration stored before it has null -- if either
        // read as unlimited, all of their links would silently become reusable.
        SignupLink.ResolveMaxRedemptions(null, null)
            .Should().Be(SignupLink.DefaultMaxRedemptions)
            .And.Be(1);
    }

    // ---------- the sentinel ----------

    [Fact]
    public void Unlimited_IsZero_NotNull()
    {
        SignupLink.UnlimitedMaxRedemptions.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0, true)]      // unlimited, never used
    [InlineData(99, 0, true)]     // unlimited, used 99 times, still has budget
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]     // single use, spent
    [InlineData(2, 5, true)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, false)]     // over cap: still exhausted, never negative budget
    public void HasRedemptionBudget_ReadsZeroAsNoCap(int count, int max, bool expected)
    {
        // A bare `count >= max` reads 0 as "exhausted on sight", which is the opposite of
        // what the sentinel means. This is the one place that distinction lives.
        SignupLink.HasRedemptionBudget(count, max).Should().Be(expected);
    }

    // ---------- payload validation ----------

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    public void Generate_AllowedMaxRedemptions(int? value)
    {
        var request = new GenerateSignupLinkRequest
        {
            ConfigurationId = "cfg-1",
            Email = "ada@example.com",
            FirstName = "Ada",
            LastName = "Lovelace",
            MaxRedemptions = value
        };

        GenerateValidator.Validate(request).Errors
            .Should().NotContain(e => e.PropertyName == nameof(GenerateSignupLinkRequest.MaxRedemptions));
    }

    [Fact]
    public void Generate_NegativeMaxRedemptions_IsRejected()
    {
        var request = new GenerateSignupLinkRequest
        {
            ConfigurationId = "cfg-1",
            Email = "ada@example.com",
            FirstName = "Ada",
            LastName = "Lovelace",
            MaxRedemptions = -1
        };

        GenerateValidator.Validate(request).Errors
            .Should().Contain(e => e.ErrorMessage == "MaxRedemptions must be 0 (unlimited) or a positive count");
    }
}
