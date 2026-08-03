using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// <see cref="CertificateProviderFactory"/> maps a <see cref="CertificateStorageType"/> to the
    /// matching provider. The Azure arm requires vault configuration that is absent in the test
    /// environment, so it surfaces an <see cref="InvalidOperationException"/> from the provider ctor.
    /// </summary>
    public class CertificateProviderFactoryTests
    {
        private readonly Mock<ILogger<CertificateProviderFactory>> _logger = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();

        private CertificateProviderFactory Create()
        {
            _blocksSecret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost:27017");
            _blocksSecret.SetupGet(s => s.RootDatabaseName).Returns("admin");
            return new CertificateProviderFactory(_logger.Object, _blocksSecret.Object);
        }

        [Fact]
        public void GetProvider_Filesystem_ReturnsFileSystemProvider()
        {
            Create().GetProvider(CertificateStorageType.Filefilesystem)
                .Should().BeOfType<FileSystemCertificateProvider>();
        }

        [Fact]
        public void GetProvider_Mongodb_ReturnsMongodbProvider()
        {
            Create().GetProvider(CertificateStorageType.Mongodb)
                .Should().BeOfType<MongodbCertificateProvider>();
        }

        [Fact]
        public void GetProvider_UnknownType_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                Create().GetProvider((CertificateStorageType)999));
        }
    }
}
