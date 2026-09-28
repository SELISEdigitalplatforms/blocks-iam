using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests;

public class SignupLinkConfigurationsControllerTests
{
    private readonly Mock<ISignupLinkConfigurationService> _config = new();
    private SignupLinkConfigurationsController Sut() => new(_config.Object);

    [Fact]
    public async Task Create_Success_ReturnsOk()
    {
        _config.Setup(s => s.CreateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "id-1" });
        (await Sut().Create(new CreateSignupLinkConfigurationRequest())).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_Failure_ReturnsBadRequest()
    {
        _config.Setup(s => s.CreateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ClientId", "Client not found or inactive" } }
            });
        (await Sut().Create(new CreateSignupLinkConfigurationRequest())).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNotFound()
    {
        _config.Setup(s => s.GetByIdAsync("missing")).ReturnsAsync((null, true));
        (await Sut().GetById("missing")).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        _config.Setup(s => s.UpdateAsync("missing", It.IsAny<UpdateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
            });
        (await Sut().Update("missing", new UpdateSignupLinkConfigurationRequest())).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Query_Success_ReturnsOk()
    {
        _config.Setup(s => s.QueryAsync(It.IsAny<QuerySignupLinkConfigurationsRequest>()))
            .ReturnsAsync((new SignupLinkConfigurationListResponse { Items = [], TotalCount = 0 }, null));
        (await Sut().Query(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 }))
            .Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Archive_Success_ReturnsOk()
    {
        _config.Setup(s => s.ArchiveAsync("id-1"))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "id-1" });
        (await Sut().Archive("id-1")).Should().BeOfType<OkObjectResult>();
    }
}

public class SignupLinksControllerTests
{
    private readonly Mock<ISignupLinkGenerationService> _generation = new();
    private SignupLinksController Sut() => new(_generation.Object);

    [Fact]
    public async Task Generate_Success_ReturnsOk()
    {
        _generation.Setup(s => s.GenerateAsync(It.IsAny<GenerateSignupLinkRequest>()))
            .ReturnsAsync(GenerateSignupLinkResult.Ok(new GenerateSignupLinkResponse
            {
                LinkId = "l1",
                Url = "https://iam.example/oidc/join/t1#link=abc",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                EmailAlreadyExists = false
            }));
        (await Sut().Generate(new GenerateSignupLinkRequest())).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Generate_OrgConflict_ReturnsBadRequest()
    {
        _generation.Setup(s => s.GenerateAsync(It.IsAny<GenerateSignupLinkRequest>()))
            .ReturnsAsync(GenerateSignupLinkResult.Fail(400, "OrganizationId", "Organization must match the caller's organization"));
        var obj = (await Sut().Generate(new GenerateSignupLinkRequest())).Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Generate_GrantRefused_ReturnsForbidden()
    {
        _generation.Setup(s => s.GenerateAsync(It.IsAny<GenerateSignupLinkRequest>()))
            .ReturnsAsync(GenerateSignupLinkResult.Fail(403, "Roles", "You cannot grant the role: tenant-admin"));
        var obj = (await Sut().Generate(new GenerateSignupLinkRequest())).Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task QueryLinks_Success_ReturnsOk()
    {
        _generation.Setup(s => s.QueryAsync(It.IsAny<QuerySignupLinksRequest>()))
            .ReturnsAsync((new SignupLinkListResponse { Items = [], TotalCount = 0 }, null));
        (await Sut().QueryLinks(new QuerySignupLinksRequest())).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Revoke_NotFound_ReturnsNotFound()
    {
        _generation.Setup(s => s.RevokeAsync("missing"))
            .ReturnsAsync(new RevokeSignupLinkResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
            });
        (await Sut().Revoke("missing")).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RevokeByConfiguration_Success_ReturnsOk()
    {
        _generation.Setup(s => s.RevokeByConfigurationAsync(It.IsAny<RevokeSignupLinksByConfigurationRequest>()))
            .ReturnsAsync(new RevokeSignupLinksByConfigurationResponse { IsSuccess = true, RevokedCount = 3 });
        (await Sut().RevokeByConfiguration(new RevokeSignupLinksByConfigurationRequest { ConfigurationId = "cfg1" }))
            .Should().BeOfType<OkObjectResult>();
    }
}
