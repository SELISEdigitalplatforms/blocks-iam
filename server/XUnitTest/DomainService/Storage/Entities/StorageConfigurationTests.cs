using CloudConfiguration.DomainService.Storage.Entities;
using Xunit;

namespace XUnitTest.DomainService.Storage.Entities
{
    public class StorageConfigurationTests
    {
        [Fact]
        public void Can_Set_And_Get_All_Properties()
        {
            var config = new StorageConfiguration
            {
                Name = "TestName",
                ConnectionString = "TestConnectionString",
                SecretKey = "TestSecretKey",
                AccessKey = "TestAccessKey",
                StorageStrategy = "TestStrategy",
                CloudStorageRegionEndPoint = "TestRegion",
                Host = "TestHost",
                Port = "1234",
                UserName = "TestUser",
                Password = "TestPassword",
                RemoteBasePath = "TestPath",
                SftpSecretKey = "TestSftpSecretKey"
            };

            Assert.Equal("TestName", config.Name);
            Assert.Equal("TestConnectionString", config.ConnectionString);
            Assert.Equal("TestSecretKey", config.SecretKey);
            Assert.Equal("TestAccessKey", config.AccessKey);
            Assert.Equal("TestStrategy", config.StorageStrategy);
            Assert.Equal("TestRegion", config.CloudStorageRegionEndPoint);
            Assert.Equal("TestHost", config.Host);
            Assert.Equal("1234", config.Port);
            Assert.Equal("TestUser", config.UserName);
            Assert.Equal("TestPassword", config.Password);
            Assert.Equal("TestPath", config.RemoteBasePath);
            Assert.Equal("TestSftpSecretKey", config.SftpSecretKey);
        }

        [Fact]
        public void Default_Values_Are_Null()
        {
            var config = new StorageConfiguration();

            Assert.Null(config.Name);
            Assert.Null(config.ConnectionString);
            Assert.Null(config.SecretKey);
            Assert.Null(config.AccessKey);
            Assert.Null(config.StorageStrategy);
            Assert.Null(config.CloudStorageRegionEndPoint);
            Assert.Null(config.Host);
            Assert.Null(config.Port);
            Assert.Null(config.UserName);
            Assert.Null(config.Password);
            Assert.Null(config.RemoteBasePath);
            Assert.Null(config.SftpSecretKey);
        }
    }
}
