using Authentication.DomainService.Authentication;
using Blocks.Genesis.Entities.Iam;
using Moq;

namespace XUnitTest.TestSupport;

/// <summary>An unknown-user guard that counts nothing: no lock, no captcha, every attempt gets a slot.</summary>
public static class UnknownLoginGuardStub
{
    public static Mock<IUnknownUserLoginGuard> Create()
    {
        var guard = new Mock<IUnknownUserLoginGuard>();
        guard.Setup(g => g.GetStateAsync(It.IsAny<string?>())).ReturnsAsync(new UnknownUserLoginState(0, false));
        guard.Setup(g => g.FailAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IdentityConfiguration>()))
            .ReturnsAsync(new UnknownUserLoginAttempt(true, false));
        return guard;
    }
}
