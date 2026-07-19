using Api.Controllers;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.ApiTests
{
    /// <summary>
    /// Unit tests for <see cref="OidcClientsController"/>. The authentication domain service is
    /// mocked; each test asserts the returned result type for the input-validation, not-found,
    /// service-error and happy-path branches.
    /// </summary>
    public class OidcClientsControllerTests
    {
        private readonly Mock<IAuthenticationDomainService> _domainService = new();

        private OidcClientsController CreateController() => new(_domainService.Object);

        // ---------- GetAll ----------

        [Fact]
        public async Task GetAll_Success_ReturnsOk()
        {
            _domainService.Setup(s => s.GetOidcClientsAsync())
                .ReturnsAsync(new GetOIDCClientsResponse { IsSuccess = true });

            var result = await CreateController().GetAll();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task GetAll_Failure_ReturnsBadRequest()
        {
            _domainService.Setup(s => s.GetOidcClientsAsync())
                .ReturnsAsync(new GetOIDCClientsResponse { IsSuccess = false });

            var result = await CreateController().GetAll();

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- GetByClientId ----------

        [Fact]
        public async Task GetByClientId_MissingId_ReturnsBadRequest()
        {
            var result = await CreateController().GetByClientId("");

            result.Should().BeOfType<BadRequestObjectResult>();
            _domainService.Verify(s => s.GetOidcClientAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetByClientId_ServiceFailure_ReturnsBadRequest()
        {
            _domainService.Setup(s => s.GetOidcClientAsync("cid"))
                .ReturnsAsync(new GetOIDCClientResponse { IsSuccess = false });

            var result = await CreateController().GetByClientId("cid");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetByClientId_NotFound_ReturnsNotFound()
        {
            _domainService.Setup(s => s.GetOidcClientAsync("cid"))
                .ReturnsAsync(new GetOIDCClientResponse { IsSuccess = true, oIDCClientCredential = null });

            var result = await CreateController().GetByClientId("cid");

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetByClientId_Found_ReturnsOk()
        {
            _domainService.Setup(s => s.GetOidcClientAsync("cid"))
                .ReturnsAsync(new GetOIDCClientResponse
                {
                    IsSuccess = true,
                    oIDCClientCredential = new OidcClientRegistration { ClientId = "cid" }
                });

            var result = await CreateController().GetByClientId("cid");

            result.Should().BeOfType<OkObjectResult>();
        }

        // ---------- Upsert ----------

        [Fact]
        public async Task Upsert_NullRequest_ReturnsBadRequest()
        {
            var result = await CreateController().Upsert(null);

            result.Should().BeOfType<BadRequestObjectResult>();
            _domainService.Verify(s => s.SaveOIDCClientAsync(It.IsAny<SaveOIDCClientRequest>()), Times.Never);
        }

        [Fact]
        public async Task Upsert_Success_ReturnsOk()
        {
            _domainService.Setup(s => s.SaveOIDCClientAsync(It.IsAny<SaveOIDCClientRequest>()))
                .ReturnsAsync(new SaveOIDCClientResponse { IsSuccess = true, ItemId = "cid" });

            var result = await CreateController().Upsert(new SaveOIDCClientRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Upsert_Failure_ReturnsBadRequest()
        {
            _domainService.Setup(s => s.SaveOIDCClientAsync(It.IsAny<SaveOIDCClientRequest>()))
                .ReturnsAsync(new SaveOIDCClientResponse { IsSuccess = false });

            var result = await CreateController().Upsert(new SaveOIDCClientRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_MissingId_ReturnsBadRequest()
        {
            var result = await CreateController().Delete("");

            result.Should().BeOfType<BadRequestObjectResult>();
            _domainService.Verify(s => s.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>()), Times.Never);
        }

        [Fact]
        public async Task Delete_Success_ReturnsOk()
        {
            _domainService.Setup(s => s.DeleteOidcClientAsync(It.Is<DeleteOIDCClientRequest>(r => r.ItemId == "cid")))
                .ReturnsAsync(new Blocks.Genesis.BaseResponse { IsSuccess = true });

            var result = await CreateController().Delete("cid");

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Delete_Failure_ReturnsBadRequest()
        {
            _domainService.Setup(s => s.DeleteOidcClientAsync(It.IsAny<DeleteOIDCClientRequest>()))
                .ReturnsAsync(new Blocks.Genesis.BaseResponse { IsSuccess = false });

            var result = await CreateController().Delete("cid");

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ---------- RotateSecret ----------

        [Fact]
        public async Task RotateSecret_MissingId_ReturnsBadRequest()
        {
            var result = await CreateController().RotateSecret("", null);

            result.Should().BeOfType<BadRequestObjectResult>();
            _domainService.Verify(s => s.RotateOidcClientSecretAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RotateSecret_Success_ReturnsOk()
        {
            _domainService.Setup(s => s.RotateOidcClientSecretAsync("cid"))
                .ReturnsAsync(new RotateOidcClientSecretResponse { IsSuccess = true, ClientSecret = "new-secret" });

            var result = await CreateController().RotateSecret("cid", null);

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task RotateSecret_Failure_ReturnsBadRequest()
        {
            _domainService.Setup(s => s.RotateOidcClientSecretAsync("cid"))
                .ReturnsAsync(new RotateOidcClientSecretResponse { IsSuccess = false });

            var result = await CreateController().RotateSecret("cid", null);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
