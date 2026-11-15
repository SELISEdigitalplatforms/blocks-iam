using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Blocks.Genesis;
using Iam.DomainService.Resources;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Auth.DisabledOrganizations
{
    /// <summary>Shared setup for the spec #668 tests: a real <see cref="JwtAccessTokenProvider"/> over a mocked status store.</summary>
    internal static class DisabledOrganizationsKit
    {
        public const string CertPassword = "pwd";

        private static readonly Lazy<byte[]> Certificate = new(() =>
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return cert.Export(X509ContentType.Pkcs12, CertPassword);
        });

        public static void MultiOrg(this Mock<IResourceRepository> repository, bool allowNoOrg = false, bool enabled = true) =>
            repository.Setup(r => r.GetTenantConfigurationAsync())
                .ReturnsAsync(new IamTenantConfiguration { IsMultiOrgEnabled = enabled, AllowNoOrgSignInForDisabledOrganizations = allowNoOrg });

        /// <summary>A real provider whose certificate comes from a cache hit, with the real claims resolver.</summary>
        public static JwtAccessTokenProvider RealProvider(Mock<IResourceRepository> repository)
        {
            var cacheDb = new Mock<IDatabase>();
            cacheDb.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(Certificate.Value);
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(cacheDb.Object);
            var crypto = new Mock<ICryptoService>();
            crypto.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>())).Returns("cache-key");

            return new JwtAccessTokenProvider(
                NullLogger<JwtAccessTokenProvider>.Instance, cache.Object, crypto.Object,
                new Mock<ICertificateProviderFactory>().Object,
                new AuthorizationClaimsResolver(new Mock<IUserRepository>().Object),
                repository.Object);
        }

        /// <summary>A tenant whose JWT parameters match <see cref="RealProvider"/>'s certificate.</summary>
        public static Tenant SigningTenant() => new()
        {
            TenantId = "tenant-1",
            ItemId = "tenant-item-1",
            Name = "t",
            DbConnectionString = "mongodb://localhost",
            Applications = new List<Applications>(),
            JwtTokenParameters = new JwtTokenParameters
            {
                Issuer = "https://issuer",
                PrivateCertificatePassword = CertPassword,
                IssueDate = DateTime.UtcNow,
                CertificateStorageType = CertificateStorageType.Azure
            }
        };
    }
}
