using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CloudConfiguration.DomainService.Authentication.Entities;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.IAM.Entities;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Mail.Entities;
using CloudConfiguration.DomainService.Mail.RequestModel;
using FluentAssertions;
using Moq;
using MongoDB.Driver;
using MongoDB.Bson;
using Xunit;
using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Shared.Services.Tests
{
    public class ConfigurationRepositoryTests
    {
        private readonly Mock<IDbContextProvider> _dbContextProviderMock = new Mock<IDbContextProvider>();

        [Fact]
        public async Task UpdateAuthenticationConfigAsync_CallsReplaceOneAsync()
        {
            var collectionMock = new Mock<IMongoCollection<AuthenticationConfiguration>>();
            _dbContextProviderMock.Setup(x => x.GetCollection<AuthenticationConfiguration>("AuthenticationConfigurations")).Returns(collectionMock.Object);
            var repo = new ConfigurationRepository(_dbContextProviderMock.Object);
            var config = new AuthenticationConfiguration { ItemId = ObjectId.GenerateNewId() };
            await repo.UpdateAuthenticationConfigAsync(config);
            collectionMock.Verify(x => x.ReplaceOneAsync(It.IsAny<FilterDefinition<AuthenticationConfiguration>>(), config, It.IsAny<ReplaceOptions>(), default), Times.Once);
        }
    }
}
