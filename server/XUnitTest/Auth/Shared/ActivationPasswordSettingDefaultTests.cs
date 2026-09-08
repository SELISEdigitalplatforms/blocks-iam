using Authentication.DomainService.Entities;
using FluentAssertions;
using Iam.DomainService.Dtos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// CollectPasswordOnActivation was added after tenants were already provisioned, so their
    /// stored configuration document has no such field. These tests pin the upgrade behaviour:
    /// a document written before the setting existed must still read as "collect a password",
    /// leaving the activation flow exactly as it was.
    /// </summary>
    public class ActivationPasswordSettingDefaultTests
    {
        /// <summary>A configuration document as written before the setting existed.</summary>
        private static BsonDocument LegacyConfigurationDocument() => new()
        {
            { "_id", ObjectId.GenerateNewId() },
            { "AccountActivationPath", "/activate" },
            { "AccountVerificationPath", "/verify" },
            { "RecoverAccountPath", "/recover" },
            { "IsOidcEnabled", true },
            { "AccountActionBaseUrl", "https://app.example.com" },
            { "LogoutOnPasswordChange", true }
        };

        [Fact]
        public void IdentityConfiguration_WithoutTheField_ReadsAsCollectingAPassword()
        {
            var configuration = BsonSerializer.Deserialize<IdentityConfiguration>(LegacyConfigurationDocument());

            configuration.CollectPasswordOnActivation.Should().BeTrue();
        }

        [Fact]
        public void IamConfiguration_WithoutTheField_ReadsAsCollectingAPassword()
        {
            var configuration = BsonSerializer.Deserialize<IamConfiguration>(LegacyConfigurationDocument());

            configuration.CollectPasswordOnActivation.Should().BeTrue();
        }

        [Fact]
        public void IdentityConfiguration_WithTheFieldStoredFalse_IsHonoured()
        {
            var document = LegacyConfigurationDocument();
            document["CollectPasswordOnActivation"] = false;

            var configuration = BsonSerializer.Deserialize<IdentityConfiguration>(document);

            configuration.CollectPasswordOnActivation.Should().BeFalse();
        }
    }
}
