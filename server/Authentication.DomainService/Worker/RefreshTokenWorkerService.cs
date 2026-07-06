using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Authentication.DomainService.Worker
{
    public sealed class RefreshTokenWorkerService : IConsumer<RefreshTokenEvent>
    {
        private readonly ILogger<RefreshTokenWorkerService> _logger;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly IUserRepository _userRepository;

        public RefreshTokenWorkerService(ILogger<RefreshTokenWorkerService> logger, IAuthenticationRepository oAuthRepository, IUserRepository userRepository)
        {
            _logger = logger;
            _oAuthRepository = oAuthRepository;
            _userRepository = userRepository;
        }
        public async Task Consume(RefreshTokenEvent context)
        {
            _logger.LogInformation("RefreshTokenWorkerService start");

            if (context.IsRevoke)
            {
                // Mark old session as inactive - no login count update, no new session
                await Task.WhenAll(
                    RevokeSessionAsync(context),
                    ProcessUserTimelineEvent(context)
                );
            }
            else if (context.IsLogin)
            {
                // Fresh login - insert new session, update login info, record timeline
                await Task.WhenAll(
                    ProcessSession(context),
                    ProcessUserTimelineEvent(context),
                    UpdateUserByLoginInfoAsync(context)
                );
            }
            else
            {
                // Token renewal - insert new session, record timeline (no login count update)
                await Task.WhenAll(
                    ProcessSession(context),
                    ProcessUserTimelineEvent(context)
                );
            }
        }

        public async Task<bool> RevokeSessionAsync(RefreshTokenEvent context)
        {
            return await _oAuthRepository.RevokeIdentitySessionAsync(context.RefreshToken, context.UserId);
        }

        public async Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenEvent)
        {
            _logger.LogInformation("User Mutation event -- initiate to update login info");

            var user = await _userRepository.GetUserByIdAsync(refreshTokenEvent.UserId);

            if (user == null)
            {
                _logger.LogError("User not found by this user id: {Id}", refreshTokenEvent.UserId);
                return;
            }

            if (user.LogInCount == 0)
            {
                user.FirstLoggedInTime = DateTime.Now;
            }

            user.LogInCount += 1;
            user.LastLoggedInTime = DateTime.Now;
            user.LastLoggedInDeviceInfo = JsonSerializer.Serialize(refreshTokenEvent.DeviceInformation);

            await _userRepository.UpdateUserAsync(user);

            _logger.LogInformation("User Mutation event -- end of the update login info");
        }

        public async Task<bool> ProcessSession(RefreshTokenEvent context)
        {
            var session = new IdentitySession
            {
                RefreshToken = context.RefreshToken,
                TenantId = context.TenantId,
                UserId = context.UserId,
                OrganizationId = context.OrganizationId,
                ClientId = context.ClientId,
                SessionId = context.SessionId,
                IssuedUtc = context.IssuedUtc,
                ExpiresUtc = context.ExpiresUtc,
                IpAddresses = context.IpAddresses,
                DeviceInformation = context.DeviceInformation,
                GrantType = context.GrantType,
                IsLogin = context.IsLogin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            return await _oAuthRepository.InsertIdentitySessionAsync(session);
        }

        public async Task<bool> ProcessUserTimelineEvent(RefreshTokenEvent context)
        {
            var eventName = context.IsRevoke
                ? "revoke_refresh_token"
                : context.IsLogin
                    ? $"login_via_{context.GrantType ?? "unknown"}"
                    : "renew_refresh_token";

            var identityEvent = new IdentityEvent
            {
                TenantId = context?.TenantId ?? string.Empty,
                UserId = context?.UserId ?? string.Empty,
                OrganizationId = context?.OrganizationId,
                DeviceInformation = context?.DeviceInformation,
                IpAddresses = context?.IpAddresses ?? string.Empty,
                Event = eventName,
                ActionBy = "RefreshTokenWorkerService",
                CreatedAt = DateTime.UtcNow
            };

            return await _oAuthRepository.InsertIdentityEventAsync(identityEvent);
        }
    }
}