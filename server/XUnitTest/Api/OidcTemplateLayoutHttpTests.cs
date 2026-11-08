using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Api.Controllers;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using XUnitTest.Auth;

namespace XUnitTest.ApiTests;

public sealed class OidcTemplateLayoutHttpTests
{
    // Exercise the actual ApiController JSON binder. The real permission guard is
    // covered by OidcTemplateControllerTests; the test host authorizes these requests.
    [Theory]
    [InlineData("\"false\"")]
    [InlineData("0")]
    [InlineData("\"yes\"")]
    public async Task Put_NonBooleanLayout_Returns400BeforeSaving(string value)
    {
        var service = new Mock<IAuthenticationDomainService>();
        await using var app = await StartAsync(service);
        var body = JsonSerializerBody();
        body["layout"] = JsonNode.Parse($$"""{"showAnimationPanel":{{value}}}""");

        var response = await app.GetTestClient().PutAsync("/oidc-template", Json(body));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        service.Verify(s => s.SaveOidcUiTemplateRequestAsync(It.IsAny<SaveOidcUiTemplateRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    [InlineData("{\"showAnimationPanel\":null}", null)]
    [InlineData("{\"showAnimationPanel\":true}", true)]
    [InlineData("{\"showAnimationPanel\":false}", false)]
    public async Task Put_OptionalLayout_BindsWithoutChangingItsValue(string? layout, bool? expected)
    {
        var service = new Mock<IAuthenticationDomainService>();
        SaveOidcUiTemplateRequest? saved = null;
        service.Setup(s => s.SaveOidcUiTemplateRequestAsync(It.IsAny<SaveOidcUiTemplateRequest>()))
            .Callback<SaveOidcUiTemplateRequest>(request => saved = request)
            .ReturnsAsync(new SaveOidcUiTemplateResponse { IsSuccess = true, ItemId = "saved" });
        await using var app = await StartAsync(service);
        var body = JsonSerializerBody();
        if (layout is not null) body["layout"] = JsonNode.Parse(layout);

        var response = await app.GetTestClient().PutAsync("/oidc-template", Json(body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        saved.Should().NotBeNull();
        saved!.Layout?.ShowAnimationPanel.Should().Be(expected);
        if (layout is null or "null") saved.Layout.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Layout_PayloadGrowthStaysBelow64Bytes(bool visible)
    {
        var body = JsonSerializerBody();
        body.Remove("layout");
        var before = Encoding.UTF8.GetByteCount(body.ToJsonString());
        body["layout"] = new JsonObject { ["showAnimationPanel"] = visible };
        var growth = Encoding.UTF8.GetByteCount(body.ToJsonString()) - before;
        growth.Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_LayoutWireContract_StaysWithin64ByteBudget(bool visible)
    {
        var template = OidcUiTemplateTestData.ValidTemplate();
        template.Layout = new OidcUiTemplateLayout { ShowAnimationPanel = visible };
        var service = new Mock<IAuthenticationDomainService>();
        service.Setup(s => s.GetOidcTemplateForManagementAsync()).ReturnsAsync(template);
        await using var app = await StartAsync(service);

        var response = await app.GetTestClient().GetAsync("/oidc-template");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var json = JsonNode.Parse(bytes)!.AsObject();
        json["template"]!["layout"]!["showAnimationPanel"]!.GetValue<bool>().Should().Be(visible);
        response.Content.Headers.ContentLength.Should().Be(bytes.Length);
        json["template"]!.AsObject().Remove("layout");
        var legacyBytes = Encoding.UTF8.GetByteCount(json.ToJsonString());
        (bytes.Length - legacyBytes).Should().BeLessThanOrEqualTo(64);
        Console.WriteLine($"showAnimationPanel={visible}: legacy body={legacyBytes} bytes; response Content-Length={bytes.Length}; growth={bytes.Length - legacyBytes} bytes");
    }

    private static JsonObject JsonSerializerBody() =>
        System.Text.Json.JsonSerializer.SerializeToNode(OidcUiTemplateTestData.ValidRequest(),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.AsObject();

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static async Task<WebApplication> StartAsync(Mock<IAuthenticationDomainService> service)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(OidcTemplateController).Assembly)
            .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyTemplateController()));
        builder.Services.AddSingleton(service.Object);
        builder.Services.AddAuthorization(options => options.AddPolicy("Protected", policy => policy.RequireAssertion(_ => true)));
        var app = builder.Build();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class OnlyTemplateController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            feature.Controllers.Add(typeof(OidcTemplateController).GetTypeInfo());
        }
    }
}
