using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Reflection;

namespace XUnitTest.ApiTests;

public class UserPasswordResetControllerTests
{
    private readonly Mock<IAdminPasswordResetService> _service = new();

    private UserPasswordResetController Sut() => new(_service.Object);

    private void Returns(BaseAccountResponse response) =>
        _service.Setup(s => s.SendResetMailAsync(It.IsAny<AdminPasswordResetRequest>())).ReturnsAsync(response);

    [Fact]
    public void SendResetMail_IsProtectedByItsOwnPermission_NotAnonymous()
    {
        var method = typeof(UserPasswordResetController).GetMethod(nameof(UserPasswordResetController.SendResetMail))!;

        var guard = method.GetCustomAttribute<ProtectedEndPointAttribute>();
        guard.Should().NotBeNull();
        guard.ResourceName.Should().Be("blocks-iam::iam::reset-user-password");
        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }

    [Fact]
    public async Task SendResetMail_GrantRefused_Returns403()
    {
        Returns(new BaseAccountResponse { Errors = AccessGrantErrors.Create() });

        var result = await Sut().SendResetMail(new AdminPasswordResetRequest { UserId = "u" });

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task SendResetMail_Refused_Returns400()
    {
        Returns(new BaseAccountResponse { Errors = new Dictionary<string, string> { { "UserId", "No user found with this id" } } });

        var result = await Sut().SendResetMail(new AdminPasswordResetRequest { UserId = "u" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
