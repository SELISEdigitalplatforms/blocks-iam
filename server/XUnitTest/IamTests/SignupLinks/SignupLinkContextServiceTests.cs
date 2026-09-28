using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkContextServiceTests
{
    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();

    private SignupLinkContextService Sut() =>
        new(_links.Object, _oidc.Object, NullLogger<SignupLinkContextService>.Instance);

    private static SignupLink ActiveLink() => new()
    {
        ItemId = "link-1",
        TenantId = "t1",
        CodeHash = SignupLinkCodeHasher.Hash("code-1"),
        Email = "asif@example.com",
        FirstName = "Asif",
        ClientId = "construct-web",
        CredentialMode = SignupLinkCredentialMode.Passwordless,
        Status = SignupLinkStatus.Active,
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
        MaxRedemptions = 1,
        RedemptionCount = 0
    };

    [Fact]
    public async Task GetContext_Valid_ReturnsMaskedFieldsWithoutOrgOrRoles()
    {
        var link = ActiveLink();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://c/cb"], true, "Construct"));

        var result = await Sut().GetContextAsync("code-1", "t1");

        result.Valid.Should().BeTrue();
        result.FirstName.Should().Be("Asif");
        result.MaskedEmail.Should().Be("as•••@example.com");
        result.ApplicationName.Should().Be("Construct");
        result.CredentialMode.Should().Be("Passwordless");
    }

    [Fact]
    public async Task GetContext_UnknownCode_ReturnsInvalid()
    {
        _links.Setup(l => l.GetByCodeHashAsync(It.IsAny<string>())).ReturnsAsync((SignupLink?)null);
        (await Sut().GetContextAsync("nope", "t1")).Valid.Should().BeFalse();
    }

    [Fact]
    public async Task GetContext_Expired_ReturnsInvalid_WithoutMutating()
    {
        var link = ActiveLink();
        link.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);

        (await Sut().GetContextAsync("code-1", "t1")).Valid.Should().BeFalse();
        _links.Verify(l => l.TryIncrementRedemptionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        _links.Verify(l => l.ReplaceAsync(It.IsAny<SignupLink>()), Times.Never);
    }

    [Fact]
    public async Task GetContext_WrongTenant_ReturnsInvalid()
    {
        var link = ActiveLink();
        _links.Setup(l => l.GetByCodeHashAsync(link.CodeHash)).ReturnsAsync(link);
        (await Sut().GetContextAsync("code-1", "other")).Valid.Should().BeFalse();
    }
}
