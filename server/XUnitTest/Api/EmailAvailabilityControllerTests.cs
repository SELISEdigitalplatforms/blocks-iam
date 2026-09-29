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

#pragma warning disable CS0618 // deprecated on purpose; these tests prove the GET form still answers

    // The GET forms are deprecated but must keep working until every caller has moved to
    // POST -- blocks-os still calls them, and removing them broke its invite flow. The
    // users/exists GET is the one that mattered: with no action at all, a stale caller fell
    // through to the SPA fallback and got 200 + index.html instead of a usable status code.
    [Fact]
    public async Task IsEmailAvailableFromQuery_ReturnsOk()
    {
        _userQuery.Setup(s => s.IsUserAvailableAsync(It.IsAny<IsEmailAvailableRequest>())).ReturnsAsync(true);
        var result = await Sut().IsEmailAvailableFromQuery(new IsEmailAvailableRequest { Email = "a@b.com" });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<IsEmailAvailableResponse>().Which.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsEmailAvailableFromQuery_MissingEmail_ReturnsBadRequest()
    {
        var result = await Sut().IsEmailAvailableFromQuery(new IsEmailAvailableRequest { Email = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
        _userQuery.Verify(s => s.IsUserAvailableAsync(It.IsAny<IsEmailAvailableRequest>()), Times.Never);
    }

    [Fact]
    public async Task IsUserExistFromQuery_ValidEmail_ReturnsOk()
    {
        _userQuery.Setup(s => s.IsUserExistAsync("a@b.com")).ReturnsAsync(new IsUserExistResponse { UserId = "u-1" });
        var result = await Sut().IsUserExistFromQuery("a@b.com");
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task IsUserExistFromQuery_MissingEmail_ReturnsBadRequest()
    {
        var result = await Sut().IsUserExistFromQuery(null);
        result.Should().BeOfType<BadRequestObjectResult>();
        _userQuery.Verify(s => s.IsUserExistAsync(It.IsAny<string>()), Times.Never);
    }
}

#pragma warning restore CS0618
