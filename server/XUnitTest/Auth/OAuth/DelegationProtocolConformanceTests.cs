using Authentication.DomainService.OAuth.Services;
using Blocks.Genesis;
using FluentAssertions;
using System.Text.Json;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// The cross-SDK conformance vector, asserted against the protocol IAM actually uses.
    /// <para>
    /// The same five inputs and the same expected signature are asserted in
    /// <c>blocks-genesis-net/src/XUnitTest/Delegation/DelegationConformanceVector.cs</c> and
    /// <c>blocks-genesis-py/tests/test_delegation_conformance.py</c>. IAM no longer keeps its own
    /// copy of the constants — they come from <c>SeliseBlocks.Genesis.OS</c>, so this file now
    /// pins the published package to the vector: if a Genesis release changes the wire contract,
    /// or the Python SDK drifts from it, a test fails instead of a production exchange.
    /// </para>
    /// </summary>
    public class DelegationProtocolConformanceTests
    {
        private const string TenantId = "tenant-abc";
        private const string DelegationId = "dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
        private const string Nonce = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";
        private const long Ts = 1739577600L;
        private const string TenantSalt = "d3f1c0de-5a17-4b0c-9e8a-1f2b3c4d5e6f";

        private const string ExpectedSignatureInput =
            "tenant-abc|dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff|0f1e2d3c4b5a69788796a5b4c3d2e1f0|1739577600";

        private const string ExpectedSignature = "c01a5f122b9793b09385796b95f00ec3ebb28528d1043dc96cf3a9fe7628d560";

        [Fact]
        public void SignatureInput_IsPipeDelimitedInExactFieldOrder()
        {
            DelegationConstants.BuildSignatureInput(TenantId, DelegationId, Nonce, Ts)
                .Should().Be(ExpectedSignatureInput);
        }

        [Fact]
        public void Signature_MatchesTheSharedVector()
        {
            DelegationSignature.Compute(ExpectedSignatureInput, TenantSalt)
                .Should().Be(ExpectedSignature);
        }

        [Fact]
        public void Signature_IsLowercaseHexOfSha256Length()
        {
            var computed = DelegationSignature.Compute(ExpectedSignatureInput, TenantSalt);

            computed.Should().HaveLength(64);
            computed.Should().Be(computed.ToLowerInvariant());
            computed.Should().MatchRegex("^[0-9a-f]+$");
        }

        [Fact]
        public void VerifySignature_AcceptsAMatchAndRejectsEverythingElse()
        {
            DelegationSignature.Verify(ExpectedSignature, ExpectedSignature).Should().BeTrue();
            DelegationSignature.Verify(ExpectedSignature, "deadbeef").Should().BeFalse();
            DelegationSignature.Verify(ExpectedSignature, null).Should().BeFalse();
            DelegationSignature.Verify(ExpectedSignature, string.Empty).Should().BeFalse();
        }

        [Fact]
        public void WireConstants_MatchBothSdks()
        {
            DelegationConstants.GrantKeyPrefix.Should().Be("delegation:");
            DelegationConstants.NonceKeyPrefix.Should().Be("nonce:");
            DelegationConstants.RedemptionKeyPrefix.Should().Be("redemption:");
            DelegationConstants.GrantIdPrefix.Should().Be("dg_");
            DelegationConstants.GrantIdRandomBytes.Should().Be(32);
            DelegationConstants.DelegationGrantTokenType
                .Should().Be("urn:blocks:params:oauth:token-type:delegation-grant");
            DelegationConstants.ClockWindowSeconds.Should().Be(60);
            DelegationConstants.NonceTtl.Should().Be(TimeSpan.FromSeconds(120));
        }

        [Fact]
        public void KeyBuilders_ProduceTheDocumentedShapes()
        {
            DelegationPolicy.GrantKey(DelegationId).Should().Be($"delegation:{DelegationId}");
            DelegationPolicy.NonceKey(DelegationId, Nonce).Should().Be($"nonce:{DelegationId}:{Nonce}");
            DelegationPolicy.RedemptionKey(DelegationId).Should().Be($"redemption:{DelegationId}");
        }

        [Fact]
        public void GrantRecord_DeserializesThePascalCaseDocumentBothSdksWrite()
        {
            const string written = """
                {"TenantId":"t","UserId":"u","OrganizationId":"o","TokenVersion":"7","SecurityStamp":"s"}
                """;

            var record = JsonSerializer.Deserialize<DelegationGrantRecord>(written);

            record.Should().NotBeNull();
            record!.TenantId.Should().Be("t");
            record.UserId.Should().Be("u");
            record.OrganizationId.Should().Be("o");
            record.TokenVersion.Should().Be("7");
            record.SecurityStamp.Should().Be("s");
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("dg_", false)]
        [InlineData("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff", false)]
        [InlineData("dg_00112233445566778899AABBCCDDEEFF00112233445566778899aabbccddeeff", false)]
        [InlineData("dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeef", false)]
        [InlineData("dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeeffa", false)]
        [InlineData("dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeezz", false)]
        [InlineData("dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff", true)]
        public void IsWellFormedGrantId_AcceptsOnlyTheExactShape(string? candidate, bool expected)
        {
            DelegationPolicy.IsWellFormedGrantId(candidate).Should().Be(expected);
        }
    }
}
