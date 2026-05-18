using Blocks.Genesis;
using Cloud.DomainService.Models;
using Cloud.DomainService.Repositories;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Services;
using Moq;
using Xunit;

namespace XUnitTest.Cloud.Services
{
    public class ApiEndpointConfigServiceTests
    {
        private readonly Mock<IApiEndpointConfigRepository> _mockRepository;
        private readonly ApiEndpointConfigService _service;

        public ApiEndpointConfigServiceTests()
        {
            _mockRepository = new Mock<IApiEndpointConfigRepository>();
            _service = new ApiEndpointConfigService(_mockRepository.Object);
        }

        [Fact]
        public async Task GetListAsync_ReturnsResponseWithData()
        {
            // Arrange
            var request = new GetApiEndpointConfigsRequest
            {
                ProjectKey = "test-project",
                Page = 1,
                PageSize = 10
            };

            var mockData = new List<ApiEndpointConfig>
            {
                new ApiEndpointConfig { ItemId = "1", Service = "Service1" },
                new ApiEndpointConfig { ItemId = "2", Service = "Service2" }
            };

            _mockRepository.Setup(r => r.GetListAsync(request))
                .ReturnsAsync((mockData, 2));

            // Act
            var response = await _service.GetListAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(2, response.TotalCount);
            Assert.Equal(1, response.Page);
            Assert.Equal(10, response.PageSize);
            Assert.Equal(2, response.Data.Count());
            _mockRepository.Verify(r => r.GetListAsync(request), Times.Once);
        }

        [Fact]
        public async Task GetListAsync_WithEmptyResult_ReturnsEmptyResponse()
        {
            // Arrange
            var request = new GetApiEndpointConfigsRequest
            {
                ProjectKey = "test-project",
                Page = 1,
                PageSize = 10
            };

            _mockRepository.Setup(r => r.GetListAsync(request))
                .ReturnsAsync((new List<ApiEndpointConfig>(), 0));

            // Act
            var response = await _service.GetListAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(0, response.TotalCount);
            Assert.Empty(response.Data);
        }

        [Fact]
        public async Task UpdateAsync_WithSuccessfulUpdate_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new UpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemId = "item-1",
                IsCaptchaRequired = true,
                IsMfaRequired = false
            };

            _mockRepository.Setup(r => r.UpdateAsync(
                    request.ProjectKey,
                    request.ItemId,
                    request.IsCaptchaRequired,
                    request.IsMfaRequired,
                    It.IsAny<string>()))
                .ReturnsAsync(true);

            // Act
            var response = await _service.UpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Empty(response.Errors);
            _mockRepository.Verify(r => r.UpdateAsync(
                request.ProjectKey,
                request.ItemId,
                request.IsCaptchaRequired,
                request.IsMfaRequired,
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_WithFailedUpdate_ReturnsErrorResponse()
        {
            // Arrange
            var request = new UpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemId = "item-1",
                IsCaptchaRequired = true,
                IsMfaRequired = false
            };

            _mockRepository.Setup(r => r.UpdateAsync(
                    request.ProjectKey,
                    request.ItemId,
                    request.IsCaptchaRequired,
                    request.IsMfaRequired,
                    It.IsAny<string>()))
                .ReturnsAsync(false);

            // Act
            var response = await _service.UpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.False(response.IsSuccess);
            Assert.Single(response.Errors);
            Assert.True(response.Errors.ContainsKey("update_failed"));
            Assert.Equal("No matching record found to update", response.Errors["update_failed"]);
        }

        [Fact]
        public async Task BulkUpdateAsync_WithSuccessfulUpdate_ReturnsSuccessResponse()
        {
            // Arrange
            var request = new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemIds = new List<string> { "item-1", "item-2", "item-3" },
                IsCaptchaRequired = true,
                IsMfaRequired = false,
                DisableAll = false
            };

            _mockRepository.Setup(r => r.BulkUpdateAsync(
                    request.ProjectKey,
                    request.ItemIds,
                    request.IsCaptchaRequired,
                    request.IsMfaRequired,
                    It.IsAny<string>()))
                .ReturnsAsync(3);

            // Act
            var response = await _service.BulkUpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Empty(response.Errors);
            _mockRepository.Verify(r => r.BulkUpdateAsync(
                request.ProjectKey,
                request.ItemIds,
                request.IsCaptchaRequired,
                request.IsMfaRequired,
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BulkUpdateAsync_WithDisableAll_SetsAllFlagsToFalse()
        {
            // Arrange
            var request = new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemIds = new List<string> { "item-1", "item-2" },
                IsCaptchaRequired = true,
                IsMfaRequired = true,
                DisableAll = true
            };

            _mockRepository.Setup(r => r.BulkUpdateAsync(
                    request.ProjectKey,
                    request.ItemIds,
                    false,
                    false,
                    It.IsAny<string>()))
                .ReturnsAsync(2);

            // Act
            var response = await _service.BulkUpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            _mockRepository.Verify(r => r.BulkUpdateAsync(
                request.ProjectKey,
                request.ItemIds,
                false,
                false,
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task BulkUpdateAsync_WithNoMatchingRecords_ReturnsErrorResponse()
        {
            // Arrange
            var request = new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemIds = new List<string> { "item-1", "item-2" },
                IsCaptchaRequired = true,
                IsMfaRequired = false,
                DisableAll = false
            };

            _mockRepository.Setup(r => r.BulkUpdateAsync(
                    request.ProjectKey,
                    request.ItemIds,
                    request.IsCaptchaRequired,
                    request.IsMfaRequired,
                    It.IsAny<string>()))
                .ReturnsAsync(0);

            // Act
            var response = await _service.BulkUpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.False(response.IsSuccess);
            Assert.Single(response.Errors);
            Assert.True(response.Errors.ContainsKey("update_failed"));
            Assert.Equal("No matching records found to update", response.Errors["update_failed"]);
        }

        [Fact]
        public async Task BulkUpdateAsync_WithEmptyItemIds_CallsRepository()
        {
            // Arrange
            var request = new BulkUpdateApiEndpointConfigRequest
            {
                ProjectKey = "test-project",
                ItemIds = new List<string>(),
                IsCaptchaRequired = true,
                IsMfaRequired = false,
                DisableAll = false
            };

            _mockRepository.Setup(r => r.BulkUpdateAsync(
                    request.ProjectKey,
                    request.ItemIds,
                    request.IsCaptchaRequired,
                    request.IsMfaRequired,
                    It.IsAny<string>()))
                .ReturnsAsync(0);

            // Act
            var response = await _service.BulkUpdateAsync(request);

            // Assert
            Assert.NotNull(response);
            Assert.False(response.IsSuccess);
            _mockRepository.Verify(r => r.BulkUpdateAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<string>()), Times.Once);
        }
    }
}
