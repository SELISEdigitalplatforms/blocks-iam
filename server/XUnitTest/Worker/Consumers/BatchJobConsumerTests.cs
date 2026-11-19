using FluentAssertions;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;
using Moq;
using Worker.Consumers;

namespace XUnitTest.Worker.Consumers
{
    public class BatchJobConsumerTests
    {
        [Fact]
        public async Task Consume_HandsTheMessageToTheBatchService()
        {
            var service = new Mock<IBatchUserService>();
            var message = new BatchJobQueuedEvent { JobId = "j", TenantId = "t" };

            await new BatchJobConsumer(service.Object).Consume(message);

            service.Verify(s => s.ProcessAsync(message), Times.Once);
        }

        [Fact]
        public void WorkerProgram_RegistersTheConsumer()
        {
            var program = File.ReadAllText(Path.Combine(RepoServerDirectory(), "Worker", "Program.cs"));

            program.Should().Contain("services.AddSingleton<IConsumer<BatchJobQueuedEvent>, BatchJobConsumer>();");
        }

        private static string RepoServerDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlocksIAM.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("server directory not found");
        }
    }
}
