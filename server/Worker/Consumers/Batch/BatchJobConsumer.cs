using Blocks.Genesis;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;

namespace Worker.Consumers
{
    /// <summary>
    /// Runs a queued batch job, and then the tenant's other queued jobs. Thin by design: the processing
    /// lives in <see cref="IBatchUserService"/>, which turns a failure inside a job into a failed job, so
    /// the message is acknowledged once.
    /// </summary>
    public class BatchJobConsumer : IConsumer<BatchJobQueuedEvent>
    {
        private readonly IBatchUserService _batchUserService;

        public BatchJobConsumer(IBatchUserService batchUserService)
        {
            _batchUserService = batchUserService;
        }

        public async Task Consume(BatchJobQueuedEvent context)
        {
            await _batchUserService.ProcessAsync(context);
        }
    }
}
