using Blocks.Genesis;
using Iam.DomainService.Batch;
using Iam.DomainService.Dtos;

namespace Worker.Consumers
{
    /// <summary>
    /// Runs the mail phase of a batch job after its import finished. Thin by design: the processing lives in
    /// <see cref="IBatchMailService"/>, which turns a failure inside the phase into a failed mail phase.
    /// </summary>
    public class BatchMailConsumer : IConsumer<BatchMailQueuedEvent>
    {
        private readonly IBatchMailService _batchMailService;

        public BatchMailConsumer(IBatchMailService batchMailService)
        {
            _batchMailService = batchMailService;
        }

        public async Task Consume(BatchMailQueuedEvent context)
        {
            await _batchMailService.ProcessAsync(context);
        }
    }
}
