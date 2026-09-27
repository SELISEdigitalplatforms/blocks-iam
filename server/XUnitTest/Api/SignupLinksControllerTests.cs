using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests;

public class SignupLinksControllerTests
{
    private readonly Mock<ISignupLinkConfigurationService> _service = new();

    private SignupLinksController Sut() => new(_service.Object);

    [Fact]
    public async Task Create_Success_ReturnsOk()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "id-1" });

        var result = await Sut().Create(new CreateSignupLinkConfigurationRequest());
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_Failure_ReturnsBadRequest()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ClientId", "Client not found or inactive" } }
            });

        var result = await Sut().Create(new CreateSignupLinkConfigurationRequest());
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNotFound()
    {
        _service.Setup(s => s.GetByIdAsync("missing")).ReturnsAsync((null, true));
        var result = await Sut().GetById("missing");
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNotFound()
    {
        _service.Setup(s => s.UpdateAsync("missing", It.IsAny<UpdateSignupLinkConfigurationRequest>()))
            .ReturnsAsync(new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "ItemId", "Not found" } }
            });

        var result = await Sut().Update("missing", new UpdateSignupLinkConfigurationRequest());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Query_Success_ReturnsOk()
    {
        _service.Setup(s => s.QueryAsync(It.IsAny<QuerySignupLinkConfigurationsRequest>()))
            .ReturnsAsync((new SignupLinkConfigurationListResponse { Items = [], TotalCount = 0 }, null));

        var result = await Sut().Query(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Archive_Success_ReturnsOk()
    {
        _service.Setup(s => s.ArchiveAsync("id-1"))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true, ItemId = "id-1" });
        var result = await Sut().Archive("id-1");
        result.Should().BeOfType<OkObjectResult>();
    }
}
