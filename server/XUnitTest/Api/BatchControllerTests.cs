using System.Reflection;
using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Batch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>Routes, permission and status codes of the batch endpoints (spec 683 §6.2, §6.3; C2).</summary>
    public class BatchControllerTests
    {
        private readonly Mock<IBatchUserService> _service = new();

        private BatchController Controller() => new(_service.Object);

        [Fact]
        public void Controller_IsUnderTheIamRoutePrefix()
        {
            typeof(BatchController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("iam");
            typeof(BatchController).GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        }

        [Theory]
        [InlineData(nameof(BatchController.SubmitUsers), typeof(HttpPostAttribute), "batch/users")]
        [InlineData(nameof(BatchController.GetJob), typeof(HttpGetAttribute), "batch/jobs/{jobId}")]
        public void Endpoints_RequireImportUsers_C2(string methodName, Type verb, string template)
        {
            var method = typeof(BatchController).GetMethod(methodName)!;

            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-iam::iam::import-users");
            ((HttpMethodAttribute)method.GetCustomAttribute(verb)!).Template.Should().Be(template);
        }

        [Fact]
        public async Task Submit_Accepted_Answers202()
        {
            var response = new BatchSubmitResponse { IsSuccess = true, JobId = "j", Status = BatchJobStatus.Queued, Total = 1 };
            _service.Setup(s => s.SubmitAsync(It.IsAny<BatchUsersRequest>())).ReturnsAsync(response);

            var result = await Controller().SubmitUsers(new BatchUsersRequest());

            result.Should().BeOfType<AcceptedResult>().Which.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Submit_EmptyBody_ReachesTheServiceAsNull()
        {
            var parameter = typeof(BatchController).GetMethod(nameof(BatchController.SubmitUsers))!.GetParameters()[0];
            parameter.GetCustomAttribute<FromBodyAttribute>()!.EmptyBodyBehavior.Should().Be(Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow);
            _service.Setup(s => s.SubmitAsync(null)).ReturnsAsync(new BatchSubmitResponse { Errors = new Dictionary<string, string> { [BatchErrorCodes.ItemsRequired] = "x" } });

            (await Controller().SubmitUsers(null)).Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Submit_WithErrors_Answers400()
        {
            var response = new BatchSubmitResponse { Errors = new Dictionary<string, string> { [BatchErrorCodes.ItemsRequired] = "x" } };
            _service.Setup(s => s.SubmitAsync(It.IsAny<BatchUsersRequest>())).ReturnsAsync(response);

            var result = await Controller().SubmitUsers(new BatchUsersRequest());

            result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetJob_Found_Answers200()
        {
            var job = new BatchJobResponse { IsSuccess = true, JobId = "j" };
            _service.Setup(s => s.GetJobAsync("j")).ReturnsAsync(job);

            var result = await Controller().GetJob("j");

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(job);
        }

        [Fact]
        public async Task GetJob_Missing_Answers404WithJobIdError_C18()
        {
            _service.Setup(s => s.GetJobAsync(It.IsAny<string>())).ReturnsAsync((BatchJobResponse?)null);

            var result = await Controller().GetJob("j");

            var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(notFound.Value)
                .Should().Be("""{"isSuccess":false,"errors":{"jobId":"Batch job not found"}}""");
        }
    }
}
