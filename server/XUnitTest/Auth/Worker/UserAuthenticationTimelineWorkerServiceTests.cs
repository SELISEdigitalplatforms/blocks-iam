using Authentication.DomainService.Worker;
using global::Authentication.DomainService.Entities;
using global::Authentication.DomainService.Services;
using global::Authentication.DomainService.Dtos;
using global::Iam.DomainService.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Auth.Worker
{
    public class UserAuthenticationTimelineWorkerServiceTests
    {
        [Fact]
        public async Task Consume_PersistsTimelineEvent()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            var worker = new UserAuthenticationTimelineWorkerService(
                NullLogger<UserAuthenticationTimelineWorkerService>.Instance,
                authRepo.Object);

            var evt = new UserAuthenticationTimelineEvent
            {
                UserId = "user-1",
                Event = "login",
                ActionBy = "system",
                IpAddresses = "127.0.0.1"
            };

            await worker.Consume(evt);

            authRepo.Verify(r => r.InsertUserAuthenticationTimelineAsync(It.Is<UserAuthenticationTimeline>(t =>
                t.UserId == "user-1" &&
                t.Event == "login" &&
                t.ActionBy == "system" &&
                t.IpAddresses == "127.0.0.1"
            )), Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_GeneratesItemId()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            var worker = new UserAuthenticationTimelineWorkerService(
                NullLogger<UserAuthenticationTimelineWorkerService>.Instance,
                authRepo.Object);

            await worker.ProcessUserTimelineEvent(new UserAuthenticationTimelineEvent
            {
                UserId = "user-1",
                Event = "login"
            });

            authRepo.Verify(r => r.InsertUserAuthenticationTimelineAsync(It.Is<UserAuthenticationTimeline>(t =>
                !string.IsNullOrEmpty(t.ItemId) &&
                t.UserId == "user-1"
            )), Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_HandlesNullContext_Gracefully()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            var worker = new UserAuthenticationTimelineWorkerService(
                NullLogger<UserAuthenticationTimelineWorkerService>.Instance,
                authRepo.Object);

            var act = async () => await worker.ProcessUserTimelineEvent(new UserAuthenticationTimelineEvent());
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_DefaultsEventAndActionBy_WhenNull()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            var worker = new UserAuthenticationTimelineWorkerService(
                NullLogger<UserAuthenticationTimelineWorkerService>.Instance,
                authRepo.Object);

            await worker.ProcessUserTimelineEvent(new UserAuthenticationTimelineEvent
            {
                UserId = "u1",
                Event = null,
                ActionBy = null
            });

            authRepo.Verify(r => r.InsertUserAuthenticationTimelineAsync(It.Is<UserAuthenticationTimeline>(t =>
                t.Event == string.Empty &&
                t.ActionBy == string.Empty
            )), Times.Once);
        }

        [Fact]
        public async Task ProcessUserTimelineEvent_PopulatesAllExtendedFields()
        {
            var authRepo = new Mock<IAuthenticationRepository>();
            authRepo.Setup(r => r.InsertUserAuthenticationTimelineAsync(It.IsAny<UserAuthenticationTimeline>()))
                .ReturnsAsync(true);

            var worker = new UserAuthenticationTimelineWorkerService(
                NullLogger<UserAuthenticationTimelineWorkerService>.Instance,
                authRepo.Object);

            await worker.ProcessUserTimelineEvent(new UserAuthenticationTimelineEvent
            {
                UserId = "user-1",
                Event = "login_success",
                ActionBy = "system",
                TenantId = "tenant-1",
                ClientId = "client-1",
                SessionId = "session-1",
                CorrelationId = "corr-1",
                Outcome = "success",
                ReasonCode = "ok",
                RiskLevel = "low",
                IpAddresses = "127.0.0.1"
            });

            authRepo.Verify(r => r.InsertUserAuthenticationTimelineAsync(It.Is<UserAuthenticationTimeline>(t =>
                t.UserId == "user-1" &&
                t.TenantId == "tenant-1" &&
                t.ClientId == "client-1" &&
                t.SessionId == "session-1" &&
                t.CorrelationId == "corr-1" &&
                t.Outcome == "success" &&
                t.ReasonCode == "ok" &&
                t.RiskLevel == "low"
            )), Times.Once);
        }
    }
}