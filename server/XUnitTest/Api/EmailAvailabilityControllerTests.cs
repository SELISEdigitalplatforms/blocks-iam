using Api.Controllers;
using FluentAssertions;
using Iam.DomainService.Users;
using Iam.DomainService.Users.RequestModel;
using Iam.DomainService.Users.ResponseModel;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests;

public class EmailAvailabilityControllerTests
{
    private readonly Mock<IUserManagementQueryService> _userQuery = new();

    private EmailAvailabilityController Sut() => new(_userQuery.Object);

    [Fact]
    public async Task IsEmailAvailable_ReturnsOk()
    {
        _userQuery.Setup(s => s.IsUserAvailableAsync(It.IsAny<IsEmailAvailableRequest>())).ReturnsAsync(true);
        var result = await Sut().IsEmailAvailable(new IsEmailAvailableRequest { Email = "a@b.com" });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<IsEmailAvailableResponse>().Which.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserExist_MissingEmail_ReturnsBadRequest()
    {
        var result = await Sut().IsUserExist(null);
        result.Should().BeOfType<BadRequestObjectResult>();
        _userQuery.Verify(s => s.IsUserExistAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IsUserExist_ValidEmail_ReturnsOk()
    {
        _userQuery.Setup(s => s.IsUserExistAsync("a@b.com")).ReturnsAsync(new IsUserExistResponse { UserId = "u-1" });
        var result = await Sut().IsUserExist(new IsEmailAvailableRequest { Email = "a@b.com" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void IsEmailAvailableGetRejected_Returns405()
    {
        var result = Sut().IsEmailAvailableGetRejected();
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(405);
    }
}
