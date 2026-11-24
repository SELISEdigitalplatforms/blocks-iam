using Blocks.Genesis;
using Iam.DomainService.Batch;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers
{
    /// <summary>
    /// Asynchronous batch operations. A batch is checked and stored here, processed by the worker, and
    /// its result read from the job.
    /// </summary>
    [ApiController]
    [Route("iam")]
    public class BatchController : ControllerBase
    {
        private readonly IBatchUserService _batchUserService;

        public BatchController(IBatchUserService batchUserService)
        {
            _batchUserService = batchUserService;
        }

        /// <summary>
        /// Create or update up to 100 users. 202 with the job id: the users are written by the worker,
        /// and the result is read from <c>batch/jobs/{jobId}</c>.
        /// </summary>
        [HttpPost("batch/users")]
        [ProtectedEndPoint(UserPermissions.ImportUsers)]
        public async Task<IActionResult> SubmitUsers([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BatchUsersRequest? request)
        {
            var result = await _batchUserService.SubmitAsync(request);
            return result.Errors is { Count: > 0 } ? BadRequest(result) : Accepted(result);
        }

        /// <summary>A batch job of the caller's tenant: status, summary and failed users.</summary>
        [HttpGet("batch/jobs/{jobId}")]
        [ProtectedEndPoint(UserPermissions.ImportUsers)]
        public async Task<IActionResult> GetJob([FromRoute] string jobId)
        {
            var job = await _batchUserService.GetJobAsync(jobId);
            if (job is null)
            {
                return NotFound(new
                {
                    isSuccess = false,
                    errors = new Dictionary<string, string> { { "jobId", "Batch job not found" } }
                });
            }

            return Ok(job);
        }
    }
}
