using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Moq;
using System.Reflection;
using Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace XUnitTest.Auth;

public class PasswordPolicyEnforcementRegressionTests
{
    private sealed class Validator(IIdentityAccessManagementRepository accounts, IIamConfigurationRepository config)
        : PasswordValidator<string>(accounts, config)
    {
        public Task<bool> Strength(string password) => BeAStrongPassword(password, default);
        public Task<bool> Blacklist(string password) => CheckBlackListPassword(password, default);
    }

    [Theory]
    [InlineData(null, "x", true)]
    [InlineData("", "x", true)]
    [InlineData("   ", "x", true)]
    [InlineData("^ABC$", "abc", true)]
    [InlineData(@"^(?=.*\d).{10,64}$", "Abcdefg1", false)]
    [InlineData(@"^(?=.*\d).{10,64}$", "Abcdefghi1", true)]
    [InlineData("^(a+)+$", null, false)]
    public async Task ExistingStrengthBehaviourIsPreserved(string? regex, string? password, bool expected)
    {
        var config = new Mock<IIamConfigurationRepository>();
        config.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration { PasswordStrengthCheckerRegex = regex! });
        var validator = new Validator(Mock.Of<IIdentityAccessManagementRepository>(), config.Object);
        (await validator.Strength(password ?? new string('a', 255) + "!")).Should().Be(expected);
    }

    [Fact]
    public async Task StructuredPolicyIsSoleAuthority_WhenEnabled_EvenWithALegacyRegexAlsoStored()
    {
        // C6: PasswordPolicyEnabled is the sole authority for the tenant; a stored legacy regex
        // that would otherwise reject the password is not additionally applied.
        var config = new Mock<IIamConfigurationRepository>();
        config.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
        {
            PasswordPolicyEnabled = true,
            PasswordPolicyMinLength = 8,
            PasswordPolicyMaxLength = 64,
            PasswordPolicyRequireNumbers = true,
            // Would reject "Sunflower7" (needs 10+ chars) if it were still consulted.
            PasswordStrengthCheckerRegex = @"^(?=.*\d).{10,64}$"
        });
        var validator = new Validator(Mock.Of<IIdentityAccessManagementRepository>(), config.Object);

        (await validator.Strength("Sunflower7")).Should().BeTrue();
    }

    [Fact]
    public async Task StructuredPolicyRejects_WhenEnabled_RegardlessOfLegacyRegex()
    {
        var config = new Mock<IIamConfigurationRepository>();
        config.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
        {
            PasswordPolicyEnabled = true,
            PasswordPolicyMinLength = 20,
            PasswordPolicyMaxLength = 64,
            // Would accept anything if it were still consulted.
            PasswordStrengthCheckerRegex = ""
        });
        var validator = new Validator(Mock.Of<IIdentityAccessManagementRepository>(), config.Object);

        (await validator.Strength("Sunflower7")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "x", true)]
    [InlineData("^ABC$", "abc", true)]
    [InlineData(@"^(?=.*\d).{10,64}$", "Abcdefg1", false)]
    public async Task LegacyRegexBehaviourIsUntouched_WhenStructuredPolicyIsNotEnabled(
        string? regex, string password, bool expected)
    {
        // C7: PasswordPolicyEnabled defaults to false, so every tenant that predates this spec
        // (or simply never turns it on) keeps today's exact regex behaviour.
        var config = new Mock<IIamConfigurationRepository>();
        config.Setup(c => c.GetConfigurationAsync()).ReturnsAsync(new IamConfiguration
        {
            PasswordPolicyEnabled = false, PasswordStrengthCheckerRegex = regex!
        });
        var validator = new Validator(Mock.Of<IIdentityAccessManagementRepository>(), config.Object);

        (await validator.Strength(password)).Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BlacklistRemainsEnforced(bool listed)
    {
        var accounts = new Mock<IIdentityAccessManagementRepository>();
        accounts.Setup(a => a.CheckPasswordBlackListedAsync("password")).ReturnsAsync(listed);
        var validator = new Validator(accounts.Object, Mock.Of<IIamConfigurationRepository>());
        (await validator.Blacklist("password")).Should().Be(!listed);
    }

    [Fact]
    public void EndpointPermissionsRemainUnchanged()
    {
        typeof(IdpController).GetMethod(nameof(IdpController.OidcUiConfig))!
            .GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(AuthenticationController).GetMethod("GetAuthenticationConfiguration")!
            .GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-iam::auth::identity-config");
        typeof(AuthenticationController).GetMethod("UpdateAuthenticationConfiguration")!
            .GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-iam::auth::mutate-identity-config");
    }
}
