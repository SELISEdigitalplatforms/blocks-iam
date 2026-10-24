using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.CaptchaDriver;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.Auth
{
    /// <summary>
    /// Lockout under parallel requests, against a real Mongo (single-document atomic updates are the
    /// thing under test, so a mock cannot prove it). All three password paths: /auth/login (password
    /// grant), /oidc/login, and the signup-link password step.
    /// </summary>
    public sealed class LoginLockoutRaceTests : IDisposable
    {
        private const string UserId = "victim";
        private const string Right = "Right#Pass1";
        private const int Threshold = 5;   // IdentityConfiguration default
        private const int BaseMinutes = 15; // IdentityConfiguration default

        private readonly RealMongo _mongo;
        private readonly IMongoCollection<User> _users;
        private readonly AuthenticationRepository _repo;
        private readonly IdentityConfiguration _config = new();

        public LoginLockoutRaceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(null);
            _mongo = new RealMongo();
            _users = _mongo.Database.GetCollection<User>("Users");
            _repo = new AuthenticationRepository(_mongo.ProviderFor(), new OidcDiscoveryClient(new Mock<IHttpClientFactory>().Object), new Mock<IKeyValueStore>().Object, new XUnitTest.TestDoubles.PassThroughIamDataAccess());
        }

        public void Dispose()
        {
            _mongo.Dispose();
            BlocksContext.IsTestMode = false;
        }

        private async Task SeedAsync(Action<User>? change = null)
        {
            var user = new User
            {
                ItemId = UserId,
                UserName = "victim@x.com",
                Email = "victim@x.com",
                Active = true,
                IsVerified = true,
                Password = BCrypt.Net.BCrypt.HashPassword(Right, 10), // real cost: a real race window
                OrganizationIds = ["default"]
            };
            change?.Invoke(user);
            await _users.InsertOneAsync(user);
            var configs = _mongo.Database.GetCollection<IdentityConfiguration>("IdentityConfigurations");
            if (await configs.CountDocumentsAsync(_ => true) == 0)
            {
                await configs.InsertOneAsync(_config); // the signup-link step reads it from the DB
            }
        }

        private Task<User> DbUserAsync() => _users.Find(u => u.ItemId == UserId).FirstAsync();

        private Task SetAsync(UpdateDefinition<User> update) => _users.UpdateOneAsync(u => u.ItemId == UserId, update);

        /// <summary>Moves time forward: the lock and the last failure lie in the past.</summary>
        private Task ExpireLockAsync() => SetAsync(Builders<User>.Update
            .Set(u => u.LockoutUntilUtc, DateTime.UtcNow.AddSeconds(-1))
            .Set(u => u.LastFailedLoginUtc, DateTime.UtcNow.AddMinutes(-20)));

        private PasswordCredentialVerifier Verifier() => new(
            NullLogger<PasswordCredentialVerifier>.Instance, new Mock<ITenants>().Object, _repo,
            new Mock<IAccountService>().Object, new Mock<IUserActivityDispatcher>().Object, new Mock<IAuthenticationDomainService>().Object);

        // ---------- /auth/login (password grant) ----------

        private PasswordAuthenticationService Login()
        {
            var tokens = new Mock<IOAuthJwtAccessTokenManager>();
            tokens.Setup(t => t.ManageTokenAsync(It.IsAny<TokenRequest>(), It.IsAny<IdentityConfiguration>(), It.IsAny<User>(), It.IsAny<StateInfo?>()))
                .ReturnsAsync(new TokenResponse { AccessToken = "ACCESS", StatusCode = 200 });
            return new PasswordAuthenticationService(NullLogger<PasswordAuthenticationService>.Instance, tokens.Object,
                new Mock<ICryptoService>().Object, _repo, Verifier(), UnknownLoginGuardStub.Create().Object);
        }

        private Task<TokenResponse> LoginAsync(PasswordAuthenticationService sut, string password) =>
            sut.AuthenticateAsync(new TokenRequest { GrantType = GrantTypes.Password, Username = "victim@x.com", Password = password }, _config);

        private static int Checked(IEnumerable<TokenResponse> r) => r.Count(x => x.Error == OAuthError.InValidUseNamePassword);
        private static int Locked(IEnumerable<TokenResponse> r) => r.Count(x => x.StatusCode == StatusCodes.Status423Locked);
        private static int Tokens(IEnumerable<TokenResponse> r) => r.Count(x => x.AccessToken == "ACCESS");

        private static Task<T[]> Burst<T>(int n, Func<int, Task<T>> call) =>
            Task.WhenAll(Enumerable.Range(0, n).Select(i => Task.Run(() => call(i))));

        [RealMongoFact]
        public async Task AuthLogin_40ParallelWrong_ExactlyThresholdChecks_RestLocked()
        {
            await SeedAsync();
            var sut = Login();

            var results = await Burst(40, i => LoginAsync(sut, "wrong" + i));

            Checked(results).Should().Be(Threshold);
            Locked(results).Should().Be(40 - Threshold);
            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(Threshold);
            db.LockoutCount.Should().Be(1);
            db.LockoutUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(BaseMinutes), TimeSpan.FromSeconds(30));

            // Correct password after the burst: locked answer, no token.
            var after = await LoginAsync(sut, Right);
            after.StatusCode.Should().Be(StatusCodes.Status423Locked);
            after.AccessToken.Should().BeNullOrEmpty();
        }

        [RealMongoFact]
        public async Task AuthLogin_CorrectPasswordInsideTheBurst_NeverMoreThanThresholdChecks_NoTokenOnceLocked()
        {
            var rng = new Random(7);
            for (var round = 0; round < 10; round++)
            {
                await _users.DeleteManyAsync(_ => true);
                await SeedAsync();
                var sut = Login();
                var rightAt = rng.Next(40);

                var results = await Burst(40, i => LoginAsync(sut, i == rightAt ? Right : "wrong" + i));

                var wrongChecks = Checked(results);
                var tokens = Tokens(results);
                // A correct password gives its slot back, so never more than the threshold of WRONG checks
                // while no one signs in. A real sign-in resets the counter (by design, see
                // Sequential_Escalation_..._ThenSuccessResets), so wrong checks still in the burst get one
                // fresh threshold after it: at most one threshold before the sign-in and one after.
                tokens.Should().BeLessThanOrEqualTo(1);
                wrongChecks.Should().BeLessThanOrEqualTo(tokens == 0 ? Threshold : 2 * Threshold, "round {0}", round);
                (wrongChecks + tokens + Locked(results)).Should().Be(40);

                // Whatever the burst did: once the account is locked, the correct password gets no token.
                if ((await DbUserAsync()).LockoutUntilUtc > DateTime.UtcNow)
                {
                    var late = await Burst(10, i => LoginAsync(sut, i % 2 == 0 ? Right : "late" + i));
                    Tokens(late).Should().Be(0);
                    Locked(late).Should().Be(10);
                }
            }
        }

        // ---------- /oidc/login ----------

        private OidcLoginOrchestrator Oidc()
        {
            var repo = new Mock<IAuthenticationRepository>();
            repo.Setup(r => r.GetOidcClientRegistrationAsync(It.IsAny<string>())).ReturnsAsync(new OidcClientRegistration { ClientId = "client-1" });
            repo.Setup(r => r.GetAuthenticationConfigurationAsync()).ReturnsAsync(_config);
            repo.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .Returns((string u, string? o) => _repo.GetUserByUsernameAsync(u, o));
            repo.Setup(r => r.TryReserveLoginAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Returns((string u, int t, DateTime n) => _repo.TryReserveLoginAttemptAsync(u, t, n));
            repo.Setup(r => r.ReleaseLoginAttemptAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Returns((string u, DateTime n) => _repo.ReleaseLoginAttemptAsync(u, n));
            repo.Setup(r => r.LockAccountIfLoginThresholdReachedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()))
                .Returns((string u, int t, int d, DateTime n) => _repo.LockAccountIfLoginThresholdReachedAsync(u, t, d, n));

            var authService = new Mock<Authentication.DomainService.Authentication.IAuthenticationService>();
            var tenants = new Mock<ITenants>();
            var cache = new Mock<ICacheClient>();
            var userRepo = new Mock<IUserRepository>();
            var endpoint = new OidcAuthorizationEndpoint(
                new OidcAuthorizationSessionStores(
                    new Mock<IAuthorizationCodeRepository>().Object,
                    new Mock<IIdpSessionRepository>().Object,
                    new Mock<Authentication.DomainService.Oidc.Services.IIdpSessionService>().Object,
                    new Mock<IPkceService>().Object,
                    new Mock<ILinkSessionRepository>().Object),
                new OidcAuthorizationIdentityStores(userRepo.Object, repo.Object, authService.Object, tenants.Object, cache.Object,
                    new Mock<IResourceRepository>().Object),
                NullLogger<OidcAuthorizationEndpoint>.Instance);
            var authDomain = new Mock<IAuthenticationDomainService>();
            authDomain.Setup(d => d.GetDeviceInfo(It.IsAny<string>())).Returns((DeviceInformation)null!);

            return new OidcLoginOrchestrator(repo.Object, new Mock<IMfaChallengeIssuer>().Object, authService.Object, tenants.Object,
                cache.Object, userRepo.Object, new PasswordHasher(NullLogger<PasswordHasher>.Instance),
                new OidcLoginAuditWriter(authDomain.Object, new Mock<IUserActivityDispatcher>().Object, NullLogger<OidcLoginAuditWriter>.Instance),
                new OidcCaptchaEvaluator(new Mock<ICaptchaEvaluator>().Object), endpoint, NullLogger<OidcLoginOrchestrator>.Instance,
                UnknownLoginGuardStub.Create().Object);
        }

        private static Task<IActionResult> OidcLoginAsync(OidcLoginOrchestrator sut, string password) =>
            sut.ExecuteAsync(new OidcLoginRequest
            {
                Username = "victim@x.com",
                Password = password,
                ClientId = "client-1",
                RedirectUri = "https://app.example.com/callback",
                TenantId = "t1"
            }, new DefaultHttpContext().Request, new DefaultHttpContext().Response);

        [RealMongoFact]
        public async Task OidcLogin_40ParallelWrong_ExactlyThresholdChecks_RestLocked()
        {
            await SeedAsync();
            var sut = Oidc();

            var results = await Burst(40, i => OidcLoginAsync(sut, "wrong" + i));

            // OIDC answers 401 for a counted wrong password and 423 for the one that locks or any refused.
            var unauthorized = results.Count(r => r is UnauthorizedObjectResult);
            var locked = results.Count(r => r is ObjectResult { StatusCode: StatusCodes.Status423Locked });
            unauthorized.Should().Be(Threshold - 1);
            locked.Should().Be(40 - (Threshold - 1));
            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(Threshold, "exactly threshold password checks ran");
            db.LockoutCount.Should().Be(1);
            db.LockoutUntilUtc.Should().BeAfter(DateTime.UtcNow);

            var after = await OidcLoginAsync(sut, Right);
            after.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        // ---------- signup-link password step ----------

        [RealMongoFact]
        public async Task SignupLinkPassword_40ParallelWrong_ExactlyThresholdChecks_RestLocked()
        {
            await SeedAsync();
            var verifier = Verifier();
            var user = await DbUserAsync(); // the orchestrator also holds one read taken before the burst

            var results = await Burst(40, i => verifier.VerifyAsync(user, "wrong" + i, null, null, null));

            results.Count(r => r.Error == OAuthError.InValidUseNamePassword).Should().Be(Threshold);
            results.Count(r => r.StatusCode == StatusCodes.Status423Locked).Should().Be(40 - Threshold);
            results.Count(r => r.AccountLockedNow).Should().Be(1);
            (await DbUserAsync()).FailedLoginCount.Should().Be(Threshold);

            var after = await verifier.VerifyAsync(user, Right, null, null, null);
            after.Succeeded.Should().BeFalse();
            after.StatusCode.Should().Be(StatusCodes.Status423Locked);
        }

        [RealMongoFact]
        public async Task AllThreePaths_InParallel_ShareOneBudget()
        {
            await SeedAsync();
            var login = Login();
            var oidc = Oidc();
            var verifier = Verifier();
            var stale = await DbUserAsync();

            await Burst(45, i => (i % 3) switch
            {
                0 => LoginAsync(login, "w" + i).ContinueWith(_ => 0),
                1 => OidcLoginAsync(oidc, "w" + i).ContinueWith(_ => 0),
                _ => verifier.VerifyAsync(stale, "w" + i, null, null, null).ContinueWith(_ => 0)
            });

            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(Threshold);
            db.LockoutCount.Should().Be(1);
            db.LockoutUntilUtc.Should().BeAfter(DateTime.UtcNow);
        }

        // ---------- unchanged sequential behaviour ----------

        [RealMongoFact]
        public async Task ResetAfterSuccess_Unchanged_ClearsAllCounters()
        {
            await SeedAsync(u =>
            {
                u.FailedLoginCount = 2;
                u.LastFailedLoginUtc = DateTime.UtcNow.AddSeconds(-5);
                u.FailedMfaCount = 1;
                u.LockoutCount = 1;
                u.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(-1);
            });
            var snapshot = await DbUserAsync();

            (await _repo.ResetFailureCountersAfterSuccessAsync(snapshot, DateTime.UtcNow)).Should().BeTrue();

            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(0);
            db.FailedMfaCount.Should().Be(0);
            db.LockoutCount.Should().Be(0);
            db.LastFailedLoginUtc.Should().BeNull();
            db.LockoutUntilUtc.Should().BeNull();
        }

        [RealMongoFact]
        public async Task ResetAfterSuccess_WrongPasswordCountedAfterTheRead_ClearsNothing()
        {
            await SeedAsync(u => { u.FailedLoginCount = 2; u.LastFailedLoginUtc = DateTime.UtcNow; });
            var snapshot = await DbUserAsync();
            await SetAsync(Builders<User>.Update.Inc(u => u.FailedLoginCount, 1));

            (await _repo.ResetFailureCountersAfterSuccessAsync(snapshot, DateTime.UtcNow)).Should().BeFalse();

            (await DbUserAsync()).FailedLoginCount.Should().Be(3);
        }

        [RealMongoFact]
        public async Task ResetAfterSuccess_AccountLockedSinceTheRead_KeepsTheLock()
        {
            await SeedAsync(u => { u.FailedLoginCount = Threshold - 1; u.LastFailedLoginUtc = DateTime.UtcNow; });
            var snapshot = await DbUserAsync();
            var until = DateTime.UtcNow.AddMinutes(BaseMinutes);
            await SetAsync(Builders<User>.Update.Set(u => u.LockoutUntilUtc, until));

            (await _repo.ResetFailureCountersAfterSuccessAsync(snapshot, DateTime.UtcNow)).Should().BeFalse();

            var db = await DbUserAsync();
            db.LockoutUntilUtc.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
            db.FailedLoginCount.Should().Be(Threshold - 1);
        }

        [RealMongoFact]
        public async Task ResetAfterSuccess_CleanSnapshot_WritesNothing()
        {
            await SeedAsync();
            var snapshot = await DbUserAsync();
            await SetAsync(Builders<User>.Update.Set(u => u.FailedLoginCount, 1));

            (await _repo.ResetFailureCountersAfterSuccessAsync(snapshot, DateTime.UtcNow)).Should().BeFalse();

            (await DbUserAsync()).FailedLoginCount.Should().Be(1);
        }

        [RealMongoFact]
        public async Task Sequential_FourWrongThenRight_SignsIn_AndResetsCounters()
        {
            await SeedAsync();
            var sut = Login();

            for (var i = 0; i < Threshold - 1; i++)
            {
                (await LoginAsync(sut, "wrong" + i)).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            }
            (await DbUserAsync()).FailedLoginCount.Should().Be(Threshold - 1);

            (await LoginAsync(sut, Right)).AccessToken.Should().Be("ACCESS");
            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(0);
            db.LockoutUntilUtc.Should().BeNull();
        }

        [RealMongoFact]
        public async Task Sequential_Escalation_x1_x3_x12_x288_ThenSuccessResets()
        {
            await SeedAsync();
            var sut = Login();

            for (var i = 0; i < Threshold; i++)
            {
                (await LoginAsync(sut, "wrong" + i)).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            }
            await AssertLockedForAsync(BaseMinutes, lockoutCount: 1);
            (await LoginAsync(sut, Right)).StatusCode.Should().Be(StatusCodes.Status423Locked);

            // After each lock expires, ONE wrong password locks again, longer each time (as before).
            foreach (var (factor, count) in new[] { (3, 2), (12, 3), (288, 4), (288, 5) })
            {
                await ExpireLockAsync();
                (await LoginAsync(sut, "again")).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
                await AssertLockedForAsync(BaseMinutes * factor, count);
            }

            await ExpireLockAsync();
            (await LoginAsync(sut, Right)).AccessToken.Should().Be("ACCESS");
            var db = await DbUserAsync();
            db.FailedLoginCount.Should().Be(0);
            db.LockoutCount.Should().Be(0);
            db.LockoutUntilUtc.Should().BeNull();
        }

        [RealMongoFact]
        public async Task Escalation_ResetsAfterSevenDays()
        {
            await SeedAsync(u =>
            {
                u.FailedLoginCount = Threshold - 1;
                u.LockoutCount = 3;
                u.LastLockoutUtc = DateTime.UtcNow.AddDays(-8);
            });

            (await LoginAsync(Login(), "wrong")).StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

            await AssertLockedForAsync(BaseMinutes, lockoutCount: 4);
        }

        [RealMongoFact]
        public async Task AfterLockExpires_ParallelBurst_GetsExactlyOneCheck_AndEscalates()
        {
            await SeedAsync();
            var sut = Login();
            await Burst(10, i => LoginAsync(sut, "wrong" + i));
            await ExpireLockAsync();

            var results = await Burst(20, i => LoginAsync(sut, "next" + i));

            Checked(results).Should().Be(1);
            Locked(results).Should().Be(19);
            await AssertLockedForAsync(BaseMinutes * 3, lockoutCount: 2);
        }

        [RealMongoFact]
        public async Task ReservationThatNeverFinishes_BlocksTheLastSlot_OnlyForTheLease()
        {
            await SeedAsync();
            // Five attempts reserved, their requests died before the password check finished.
            for (var i = 0; i < Threshold; i++)
            {
                (await _repo.TryReserveLoginAttemptAsync(UserId, Threshold, DateTime.UtcNow)).Should().NotBeNull();
            }
            (await _repo.TryReserveLoginAttemptAsync(UserId, Threshold, DateTime.UtcNow)).Should().BeNull();
            var sut = Login();
            (await LoginAsync(sut, Right)).StatusCode.Should().Be(StatusCodes.Status423Locked);

            // Not a permanent lock: after the lease, one attempt is allowed again.
            await SetAsync(Builders<User>.Update.Set(u => u.LastFailedLoginUtc,
                DateTime.UtcNow - AuthenticationRepository.LoginAttemptLease - TimeSpan.FromSeconds(1)));
            (await LoginAsync(sut, Right)).AccessToken.Should().Be("ACCESS");
            (await DbUserAsync()).FailedLoginCount.Should().Be(0);
        }

        [RealMongoFact]
        public async Task CorrectPassword_GivesItsReservationBack_SoMfaUsersAreNotCounted()
        {
            await SeedAsync(u => u.FailedLoginCount = 2);
            var verifier = Verifier();

            // The signup-link step and MFA users stop after the password: no reset yet.
            for (var i = 0; i < 10; i++)
            {
                (await verifier.VerifyAsync(await DbUserAsync(), Right, null, null, null)).Succeeded.Should().BeTrue();
            }

            (await DbUserAsync()).FailedLoginCount.Should().Be(2);
        }

        private async Task AssertLockedForAsync(int minutes, int lockoutCount)
        {
            var db = await DbUserAsync();
            db.LockoutCount.Should().Be(lockoutCount);
            db.LockoutUntilUtc.Should().NotBeNull();
            (db.LockoutUntilUtc!.Value - db.LastLockoutUtc!.Value).TotalMinutes.Should().BeApproximately(minutes, 0.01);
        }
    }
}
