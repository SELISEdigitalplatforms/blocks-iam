using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using MongoDB.Driver;
using Cloud.DomainService.Models;
using Cloud.DomainService.Repositories;
using Cloud.DomainService.Requests;
using Blocks.Genesis;

namespace Cloud.DomainService.Tests.Repositories
{
    public class ApiEndpointConfigRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProviderMock;
        private readonly Mock<IBlocksSecret> _blocksSecretMock;
        private readonly Mock<IMongoDatabase> _mongoDatabaseMock;
        private readonly Mock<IMongoCollection<ApiEndpointConfig>> _collectionMock;
        private readonly ApiEndpointConfigRepository _repository;

        public ApiEndpointConfigRepositoryTests()
        {
            _dbContextProviderMock = new Mock<IDbContextProvider>();
            _blocksSecretMock = new Mock<IBlocksSecret>();
            _mongoDatabaseMock = new Mock<IMongoDatabase>();
            _collectionMock = new Mock<IMongoCollection<ApiEndpointConfig>>();

            _blocksSecretMock.Setup(x => x.DatabaseConnectionString).Returns("test-connection-string");
            _dbContextProviderMock.Setup(x => x.GetDatabase(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(_mongoDatabaseMock.Object);
            _mongoDatabaseMock.Setup(x => x.GetCollection<ApiEndpointConfig>(It.IsAny<string>(), null))
                .Returns(_collectionMock.Object);

            _repository = new ApiEndpointConfigRepository(_dbContextProviderMock.Object, _blocksSecretMock.Object);
        }
/*
        [Fact]
        public async Task GetListAsync_ReturnsDataAndCount()
        {
            // Arrange
            var request = new GetApiEndpointConfigsRequest
            {
                ProjectKey = "project1",
                Page = 0,
                PageSize = 10,
                Filter = new ApiEndpointConfigFilter()
            };
            var data = new List<ApiEndpointConfig> { new ApiEndpointConfig() };
            var asyncCursorMock = new Mock<IAsyncCursor<ApiEndpointConfig>>();
            asyncCursorMock.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>())).Returns(true).Returns(false);
            asyncCursorMock.SetupGet(x => x.Current).Returns(data);

            var findFluentMock = new Mock<IFindFluent<ApiEndpointConfig, ApiEndpointConfig>>();
            findFluentMock.Setup(x => x.Skip(It.IsAny<int>())).Returns(findFluentMock.Object);
            findFluentMock.Setup(x => x.Limit(It.IsAny<int>())).Returns(findFluentMock.Object);
            findFluentMock.Setup(x => x.ToCursorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(asyncCursorMock.Object);
            _collectionMock.Setup(x => x.Find(It.IsAny<FilterDefinition<ApiEndpointConfig>>(), null)).Returns(findFluentMock.Object);
            _collectionMock.Setup(x => x.CountDocumentsAsync(It.IsAny<FilterDefinition<ApiEndpointConfig>>(), null, default)).ReturnsAsync(1);

            // Act
            var (result, count) = await _repository.GetListAsync(request);

            // Assert
            Xunit.Assert.Single(result);
            Xunit.Assert.Equal(1, count);
        }
*/
        [Fact]
        public async Task UpdateAsync_ReturnsTrue_WhenModified()
        {
            // Arrange
            var updateResult = new Mock<UpdateResult>();
            updateResult.Setup(x => x.ModifiedCount).Returns(1);
            _collectionMock.Setup(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                null,
                default)).ReturnsAsync(updateResult.Object);

            // Act
            var result = await _repository.UpdateAsync("project1", "item1", true, false, "user1");

            // Assert
            Xunit.Assert.True(result);
        }

        [Fact]
        public async Task UpdateAsync_ReturnsFalse_WhenNotModified()
        {
            // Arrange
            var updateResult = new Mock<UpdateResult>();
            updateResult.Setup(x => x.ModifiedCount).Returns(0);
            _collectionMock.Setup(x => x.UpdateOneAsync(
                It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                null,
                default)).ReturnsAsync(updateResult.Object);

            // Act
            var result = await _repository.UpdateAsync("project1", "item1", true, false, "user1");

            // Assert
            Xunit.Assert.False(result);
        }

        [Fact]
        public async Task BulkUpdateAsync_ReturnsModifiedCount()
        {
            // Arrange
            var updateResult = new Mock<UpdateResult>();
            updateResult.Setup(x => x.ModifiedCount).Returns(2);
            _collectionMock.Setup(x => x.UpdateManyAsync(
                It.IsAny<FilterDefinition<ApiEndpointConfig>>(),
                It.IsAny<UpdateDefinition<ApiEndpointConfig>>(),
                null,
                default)).ReturnsAsync(updateResult.Object);

            // Act
            var result = await _repository.BulkUpdateAsync("project1", new List<string> { "item1", "item2" }, true, false, "user1");

            // Assert
            Xunit.Assert.Equal(2, result);
        }
    }
}
