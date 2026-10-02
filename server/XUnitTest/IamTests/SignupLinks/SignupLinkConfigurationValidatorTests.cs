using FluentAssertions;
using Iam.DomainService.SignupLinks;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkConfigurationValidatorTests
{
    private readonly CreateSignupLinkConfigurationValidator _create = new();
    private readonly UpdateSignupLinkConfigurationValidator _update = new();
    private readonly QuerySignupLinkConfigurationsValidator _query = new();

    [Fact]
    public async Task Create_ValidRequest_Passes()
    {
        var result = await _create.ValidateAsync(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("https://evil.example.com")]
    [InlineData("projects")]
    public async Task Create_InvalidForwardedTo_Fails(string value)
    {
        var result = await _create.ValidateAsync(Valid(r => r.DefaultForwardedTo = value));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultForwardedTo");
    }

    [Fact]
    public async Task Create_RelativeForwardedTo_Passes()
    {
        var result = await _create.ValidateAsync(Valid(r => r.DefaultForwardedTo = "/projects"));
        result.Errors.Should().NotContain(e => e.PropertyName == "DefaultForwardedTo");
    }

    [Theory]
    [InlineData(0)]   // unlimited -- redeemable until the link expires
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Create_MaxRedemptions_AcceptsZeroAndAnyPositiveCount(int value)
    {
        var result = await _create.ValidateAsync(Valid(r => r.DefaultMaxRedemptions = value));
        result.Errors.Should().NotContain(e => e.PropertyName == "DefaultMaxRedemptions");
    }

    [Fact]
    public async Task Create_NegativeMaxRedemptions_Fails()
    {
        var result = await _create.ValidateAsync(Valid(r => r.DefaultMaxRedemptions = -1));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultMaxRedemptions");
    }

    [Fact]
    public async Task Create_MaxRedemptionsOne_Passes()
    {
        var result = await _create.ValidateAsync(Valid(r => r.DefaultMaxRedemptions = 1));
        result.Errors.Should().NotContain(e => e.PropertyName == "DefaultMaxRedemptions");
    }

    [Fact]
    public async Task Create_TooManyPermissions_Fails()
    {
        var perms = Enumerable.Range(0, 51).Select(i => $"p{i}").ToList();
        var result = await _create.ValidateAsync(Valid(r => r.DefaultPermissions = perms));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DefaultPermissions");
    }

    [Fact]
    public async Task Create_MissingCredentialMode_Fails()
    {
        var result = await _create.ValidateAsync(Valid(r => r.CredentialMode = null));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CredentialMode");
    }

    [Fact]
    public async Task Create_TtlOutOfRange_Fails()
    {
        var low = await _create.ValidateAsync(Valid(r => r.DefaultLifetimeMinutes = 4));
        low.IsValid.Should().BeFalse();
        var high = await _create.ValidateAsync(Valid(r => r.DefaultLifetimeMinutes = 10081));
        high.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Query_PageSizeBounds()
    {
        (await _query.ValidateAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 0 }))
            .IsValid.Should().BeFalse();
        (await _query.ValidateAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 101 }))
            .IsValid.Should().BeFalse();
        (await _query.ValidateAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 }))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Update_OmitsUnspecifiedFields()
    {
        var result = await _update.ValidateAsync(new UpdateSignupLinkConfigurationRequest { Description = "only" });
        result.IsValid.Should().BeTrue();
    }

    private static CreateSignupLinkConfigurationRequest Valid(Action<CreateSignupLinkConfigurationRequest>? tweak = null)
    {
        var req = new CreateSignupLinkConfigurationRequest
        {
            Name = "Construct Site Manager",
            ClientId = "construct-web",
            RedirectUri = "https://construct.example.com/callback",
            CredentialMode = SignupLinkCredentialMode.PasswordRequired,
            DefaultForwardedTo = "/projects",
            DefaultLifetimeMinutes = 1440
        };
        tweak?.Invoke(req);
        return req;
    }
}
