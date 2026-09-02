using System.Reflection;
using Api.Controllers;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using XUnitTest.Auth;

namespace XUnitTest.ApiTests
{
    public sealed class OidcTemplateControllerTests
    {
        private readonly Mock<IAuthenticationDomainService> _service = new();

        private OidcTemplateController Create() => new(_service.Object);

        [Fact]
        public async Task Get_ReturnsStoredTemplateWithOk()
        {
            var template = OidcUiTemplateTestData.ValidTemplate();
            _service.Setup(s => s.GetOidcTemplateForManagementAsync()).ReturnsAsync(template);

            var result = await Create().GetOidcTemplate();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<GetOidcUiTemplateResponse>().Subject;
            response.Template.Should().BeSameAs(template);
        }

        [Fact]
        public async Task Get_WithoutStoredTemplate_ReturnsResponseWithNullTemplate()
        {
            _service.Setup(s => s.GetOidcTemplateForManagementAsync()).ReturnsAsync((Authentication.DomainService.Entities.OidcUiTemplate?)null);

            var result = await Create().GetOidcTemplate();

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeOfType<GetOidcUiTemplateResponse>().Subject;
            response.Template.Should().BeNull();
        }

        [Fact]
        public async Task Put_WhenSaveSucceeds_ReturnsResponseWithOk()
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            var response = new SaveOidcUiTemplateResponse { IsSuccess = true, ItemId = "template-id" };
            _service.Setup(s => s.SaveOidcUiTemplateRequestAsync(request)).ReturnsAsync(response);

            var result = await Create().SaveOidcTemplate(request);

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Put_WhenValidationOrWriteFails_ReturnsSameResponseWithBadRequest()
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            var response = new SaveOidcUiTemplateResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { ["Theme.Dark.Primary"] = "invalid" }
            };
            _service.Setup(s => s.SaveOidcUiTemplateRequestAsync(request)).ReturnsAsync(response);

            var result = await Create().SaveOidcTemplate(request);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Put_WithNullBody_ReturnsBadRequestBeforeCallingService()
        {
            var result = await Create().SaveOidcTemplate(null!);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            var response = badRequest.Value.Should().BeOfType<SaveOidcUiTemplateResponse>().Subject;
            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Request");
            _service.Verify(s => s.SaveOidcUiTemplateRequestAsync(It.IsAny<SaveOidcUiTemplateRequest>()), Times.Never);
        }

        [Fact]
        public void Controller_UsesResourceRouteAndExactReadWritePermissionGuards()
        {
            typeof(OidcTemplateController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("oidc-template");

            var get = typeof(OidcTemplateController).GetMethod(nameof(OidcTemplateController.GetOidcTemplate))!;
            get.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
            get.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName
                .Should().Be("blocks-iam::iam::oidc-clients");

            var put = typeof(OidcTemplateController).GetMethod(nameof(OidcTemplateController.SaveOidcTemplate))!;
            put.GetCustomAttribute<HttpPutAttribute>().Should().NotBeNull();
            put.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName
                .Should().Be("blocks-iam::iam::mutate-oidc-clients");
        }
    }
}
