using System.Text.Json;
using Api.Controllers;
using Authentication.DomainService.SignupLinks;
using FluentAssertions;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests;

/// <summary>#593: the password step endpoint passes the body and tenant through unchanged.</summary>
public class SignupLinkRedemptionControllerTests
{
    private readonly Mock<ISignupLinkContextService> _context = new();
    private readonly Mock<ISignupLinkRedemptionOrchestrator> _orchestrator = new();

    private SignupLinkRedemptionController Sut() => new(_context.Object, _orchestrator.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task RedeemAuthenticate_ForwardsTheBody_AndUsesTheBlocksKeyWhenNoContextTenant()
    {
        var expected = new OkObjectResult(new { authorizeUrl = "https://iam/authorize" });
        _orchestrator.Setup(o => o.AuthenticateAsync("rid-1", "Correct#1", "cap", "t1",
                It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
            .ReturnsAsync(expected);

        var result = await Sut().RedeemAuthenticate(
            new RedeemSignupLinkAuthenticateRequest { RedemptionId = "rid-1", Password = "Correct#1", CaptchaCode = "cap" },
            "t1");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RedeemAuthenticate_NullBody_PassesNulls()
    {
        var expected = new BadRequestObjectResult(new { error = "invalid_request" });
        _orchestrator.Setup(o => o.AuthenticateAsync(null, null, null, It.IsAny<string?>(),
                It.IsAny<HttpRequest>(), It.IsAny<HttpResponse>()))
            .ReturnsAsync(expected);

        (await Sut().RedeemAuthenticate(null, null)).Should().BeSameAs(expected);
    }

    [Fact]
    public void AuthenticateRequest_ReadsCaptchaCodeFromSnakeCase()
    {
        var request = JsonSerializer.Deserialize<RedeemSignupLinkAuthenticateRequest>(
            """{"redemptionId":"rid-1","password":"pw","captcha_code":"cap"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request!.RedemptionId.Should().Be("rid-1");
        request.Password.Should().Be("pw");
        request.CaptchaCode.Should().Be("cap");
    }
}
