using Blocks.Genesis;
using DomainService.Migration;
using DomainService.Migration.Services;
using DomainService.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Api.Controllers
{
    /// <summary>
    /// Controller for handling migration operations between projects.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class MigrationController : ControllerBase
    {
        private readonly IMigrationService _migrationService;
        private readonly IMigrationRepository _migrationRepository;
        private readonly IMessageClient _messageClient;

        /// <summary>
        /// Initializes a new instance of the MigrationController.
        /// </summary>
        /// <param name="migrationService">The migration service.</param>
        /// <param name="migrationRepository">The migration repository.</param>
        public MigrationController(IMigrationService migrationService, IMigrationRepository migrationRepository, IMessageClient messageClient)
        {
            _migrationService = migrationService;
            _migrationRepository = migrationRepository;
            _messageClient = messageClient;
        }

        /// <summary>
        /// Initiates the migration process for the specified project and services.
        /// </summary>
        /// <param name="command">The migration request containing project and service details.</param>
        /// <returns>An <see cref="OtpGenerationResponse"/> containing the verification ID for OTP validation.</returns>
        [HttpPost]
        [ProtectedEndPoint]
        public async Task<OtpGenerationResponse> Migrate([FromBody] MigrationRequest command)
        {
            return await _migrationService.Migrate(command);
        }

        /// <summary>
        /// Verifies the OTP code for the migration process.
        /// </summary>
        /// <param name="command">The OTP verification request containing the verification ID and code.</param>
        /// <returns>An <see cref="OtpVerificationResponse"/> indicating whether the OTP is valid.</returns>
        [HttpPost]
        [ProtectedEndPoint]
        public async Task<OtpVerificationResponse> Verify([FromBody] VerifyOtpRequest request)
        {
            return await _migrationService.VerifyAsync(request);
        }

        /// <summary>
        /// Gets the migration status for projects with incomplete services.
        /// </summary>
        /// <param name="tenantGroupId">The tenant group ID to check for migrations.</param>
        /// <returns>List of migration trackers with incomplete services.</returns>
        [HttpGet]
        [ProtectedEndPoint]
        public async Task<IActionResult> GetMigrationStatus([FromQuery] string tenantGroupId)
        {
            if (string.IsNullOrEmpty(tenantGroupId))
            {
                return BadRequest("Tenant group ID cannot be empty.");
            }

            var trackers = await _migrationRepository.GetMigrationsByTenantGroupIdAsync(tenantGroupId);
            return Ok(trackers);
        }

        [HttpPost]
        [ProtectedEndPoint]
        public async Task<IActionResult> DataCleanup([FromBody] DataCleanupRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProjectKey))
            {
                return BadRequest("ProjectKey is required.");
            }
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<DataCleanupRequest>
                {
                    ConsumerName = IdentifierConstants.DataCleanupQueue,
                    Payload = request
                }
            );
            return Ok();
        }
    }
}
