using CloudConfiguration.DomainService.Storage.RequestModel;
using Xunit;

namespace XUnitTest.DomainService.Storage.RequestModel
{
    public class SaveStorageConfigurationRequestTests
    {
        [Fact]
        public void Can_Set_And_Get_All_Properties()
        {
            var request = new SaveStorageConfigurationRequest
            {
                Name = "TestName",
                ConnectionString = "TestConnectionString",
                SecretKey = "TestSecretKey",
                AccessKey = "TestAccessKey",
                StorageStrategy = "TestStrategy",
                CloudStorageRegionEndPoint = "TestRegion",
                ProjectKey = "TestProjectKey",
                UpdateRequest = true,
                ItemId = "TestItemId",
                Host = "TestHost",
                Port = "1234",
                UserName = "TestUser",
                Password = "TestPassword",
                RemoteBasePath = "TestPath"
            };

            Assert.Equal("TestName", request.Name);
            Assert.Equal("TestConnectionString", request.ConnectionString);
            Assert.Equal("TestSecretKey", request.SecretKey);
            Assert.Equal("TestAccessKey", request.AccessKey);
            Assert.Equal("TestStrategy", request.StorageStrategy);
            Assert.Equal("TestRegion", request.CloudStorageRegionEndPoint);
            Assert.Equal("TestProjectKey", request.ProjectKey);
            Assert.True(request.UpdateRequest);
            Assert.Equal("TestItemId", request.ItemId);
            Assert.Equal("TestHost", request.Host);
            Assert.Equal("1234", request.Port);
            Assert.Equal("TestUser", request.UserName);
            Assert.Equal("TestPassword", request.Password);
            Assert.Equal("TestPath", request.RemoteBasePath);
        }

        [Fact]
        public void Default_Values_Are_Null_Or_False()
        {
            var request = new SaveStorageConfigurationRequest();

            Assert.Null(request.Name);
            Assert.Null(request.ConnectionString);
            Assert.Null(request.SecretKey);
            Assert.Null(request.AccessKey);
            Assert.Null(request.CloudStorageRegionEndPoint);
            Assert.Null(request.ItemId);
            Assert.Null(request.Host);
            Assert.Null(request.Port);
            Assert.Null(request.UserName);
            Assert.Null(request.Password);
            Assert.Null(request.RemoteBasePath);
            Assert.False(request.UpdateRequest);
        }
    }
}
