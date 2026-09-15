using FluentAssertions;
using Iam.DomainService.Configurations;

namespace XUnitTest.Auth;

public class PasswordPolicyValidatorTests
{
    // ---------- ValidateAdminInput (V1-V3) ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public void RejectsMinLengthOutOfRange(int minLength)
    {
        var errors = PasswordPolicyValidator.ValidateAdminInput(minLength, 64, "");
        errors.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("PasswordPolicyMinLength", "PasswordPolicyMinLength_Out_Of_Range"));
    }

    [Theory]
    [InlineData(1, 257)]
    [InlineData(20, 12)]
    public void RejectsMaxLengthOutOfRangeOrBelowMin(int minLength, int maxLength)
    {
        var errors = PasswordPolicyValidator.ValidateAdminInput(minLength, maxLength, "");
        errors.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("PasswordPolicyMaxLength", "PasswordPolicyMaxLength_Out_Of_Range"));
    }

    [Fact]
    public void RejectsMessageOverFiveHundredCharacters()
    {
        var errors = PasswordPolicyValidator.ValidateAdminInput(8, 64, new string('m', 501));
        errors.Should().ContainSingle().Which.Should().Be(
            new KeyValuePair<string, string>("PasswordPolicyMessage", "PasswordPolicyMessage_Too_Long"));
    }

    [Fact]
    public void MinLengthFailureWinsOverALaterMaxLengthFailure()
    {
        // First failure wins: an invalid MinLength is reported even though MaxLength (12) is
        // also below the (invalid) MinLength (20).
        var errors = PasswordPolicyValidator.ValidateAdminInput(0, 12, "");
        errors.Keys.Should().ContainSingle("PasswordPolicyMinLength");
    }

    [Fact]
    public void AcceptsBoundaryValues()
    {
        PasswordPolicyValidator.ValidateAdminInput(1, 256, new string('m', 500)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(8, 64)]
    [InlineData(1, 1)]
    [InlineData(256, 256)]
    public void AcceptsOrdinaryRanges(int min, int max)
    {
        PasswordPolicyValidator.ValidateAdminInput(min, max, "A helpful hint").Should().BeEmpty();
    }

    // ---------- IsPasswordCompliant ----------

    private static PasswordPolicySnapshot Snapshot(
        int minLength = 1,
        int maxLength = 256,
        bool requireUppercase = false,
        bool requireLowercase = false,
        bool requireNumbers = false,
        bool requireSpecialChars = false) => new(
            minLength, maxLength, requireUppercase, requireLowercase, requireNumbers, requireSpecialChars);

    [Theory]
    [InlineData("short", false)]
    [InlineData("longenough", true)]
    public void EnforcesMinLength(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(minLength: 10)).Should().Be(expected);

    [Theory]
    [InlineData("withinbounds", true)]
    [InlineData("waytoolongforthepolicylimit", false)]
    public void EnforcesMaxLength(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(maxLength: 12)).Should().Be(expected);

    [Theory]
    [InlineData("Password", true)]
    [InlineData("password", false)]
    public void EnforcesRequireUppercase(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(requireUppercase: true)).Should().Be(expected);

    [Theory]
    [InlineData("password", true)]
    [InlineData("PASSWORD", false)]
    public void EnforcesRequireLowercase(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(requireLowercase: true)).Should().Be(expected);

    [Theory]
    [InlineData("password1", true)]
    [InlineData("password", false)]
    public void EnforcesRequireNumbers(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(requireNumbers: true)).Should().Be(expected);

    [Theory]
    [InlineData("password!", true)]
    [InlineData("password1", false)]
    public void EnforcesRequireSpecialChars(string password, bool expected)
        => PasswordPolicyValidator.IsPasswordCompliant(password, Snapshot(requireSpecialChars: true)).Should().Be(expected);

    [Fact]
    public void RequiresEveryEnabledFlagAtOnce()
    {
        var policy = Snapshot(minLength: 8, requireUppercase: true, requireLowercase: true,
            requireNumbers: true, requireSpecialChars: true);

        PasswordPolicyValidator.IsPasswordCompliant("Sunflower7!", policy).Should().BeTrue();
        PasswordPolicyValidator.IsPasswordCompliant("sunflower7!", policy).Should().BeFalse(); // no uppercase
        PasswordPolicyValidator.IsPasswordCompliant("SUNFLOWER7!", policy).Should().BeFalse(); // no lowercase
        PasswordPolicyValidator.IsPasswordCompliant("Sunflowers!", policy).Should().BeFalse(); // no number
        PasswordPolicyValidator.IsPasswordCompliant("Sunflower77", policy).Should().BeFalse(); // no special char
    }

    [Fact]
    public void TreatsNullPasswordAsEmpty()
        => PasswordPolicyValidator.IsPasswordCompliant(null, Snapshot(minLength: 1)).Should().BeFalse();

    // ---------- C9: ASCII-only classification ----------

    [Fact]
    public void TreatsNonAsciiLettersAsSpecialCharacters_NotAsLettersOrDigits()
    {
        // "café" is entirely non-ASCII-letter/digit once you require every class: the accented
        // "é" is neither an ASCII uppercase/lowercase letter nor a digit, so it only satisfies
        // requireSpecialChars, and the password still fails requireUppercase/requireNumbers.
        var requiresSpecial = Snapshot(requireSpecialChars: true);
        PasswordPolicyValidator.IsPasswordCompliant("café", requiresSpecial).Should().BeTrue();

        var requiresUppercaseOnly = Snapshot(requireUppercase: true);
        PasswordPolicyValidator.IsPasswordCompliant("café", requiresUppercaseOnly).Should().BeFalse();

        var requiresNumbersOnly = Snapshot(requireNumbers: true);
        PasswordPolicyValidator.IsPasswordCompliant("café", requiresNumbersOnly).Should().BeFalse();
    }

    [Fact]
    public void FromReadsEveryFieldOffTheStoredConfiguration()
    {
        var config = new Iam.DomainService.Dtos.IamConfiguration
        {
            PasswordPolicyMinLength = 12,
            PasswordPolicyMaxLength = 40,
            PasswordPolicyRequireUppercase = true,
            PasswordPolicyRequireLowercase = true,
            PasswordPolicyRequireNumbers = true,
            PasswordPolicyRequireSpecialChars = true
        };

        var snapshot = PasswordPolicySnapshot.From(config);

        snapshot.MinLength.Should().Be(12);
        snapshot.MaxLength.Should().Be(40);
        snapshot.RequireUppercase.Should().BeTrue();
        snapshot.RequireLowercase.Should().BeTrue();
        snapshot.RequireNumbers.Should().BeTrue();
        snapshot.RequireSpecialChars.Should().BeTrue();
    }
}
