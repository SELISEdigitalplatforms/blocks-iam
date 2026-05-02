using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Blocks.Genesis.Auth;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace DomainService.Oidc.Repositories
{
    public class OAuthClientRepository : IOAuthClientRepository
    {
        private readonly IMongoDatabase _database;
        private readonly ILogger<OAuthClientRepository> _logger;

        public OAuthClientRepository(IMongoDatabase database, ILogger<OAuthClientRepository> logger)
        {
            _database = database;
            _logger = logger;
        }

        public async Task<string> CreateAsync(OAuthClientModel client)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                await collection.InsertOneAsync(client);
                _logger.LogInformation($"OAuth client created: {client.ClientId}");
                return client.ClientId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating OAuth client: {client.ClientId}");
                throw;
            }
        }

        public async Task<OAuthClientModel> GetByClientIdAsync(string clientId, string tenantId)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                var filter = Builders<OAuthClientModel>.Filter.And(
                    Builders<OAuthClientModel>.Filter.Eq(c => c.ClientId, clientId),
                    Builders<OAuthClientModel>.Filter.Eq(c => c.TenantId, tenantId)
                );
                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching OAuth client: {clientId}");
                throw;
            }
        }

        public async Task<bool> ValidateClientSecretAsync(string clientId, string clientSecret)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                var filter = Builders<OAuthClientModel>.Filter.Eq(c => c.ClientId, clientId);
                var client = await collection.Find(filter).FirstOrDefaultAsync();

                if (client == null)
                {
                    _logger.LogWarning($"Client not found for secret validation: {clientId}");
                    return false;
                }

                // Use constant-time comparison to prevent timing attacks
                return ConstantTimeEquals(clientSecret, client.ClientSecret);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating client secret: {clientId}");
                throw;
            }
        }

        public async Task<bool> ValidateRedirectUriAsync(string clientId, string redirectUri)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                var filter = Builders<OAuthClientModel>.Filter.Eq(c => c.ClientId, clientId);
                var client = await collection.Find(filter).FirstOrDefaultAsync();

                if (client == null)
                {
                    _logger.LogWarning($"Client not found for redirect_uri validation: {clientId}");
                    return false;
                }

                return client.RedirectUris.Contains(redirectUri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating redirect_uri: {clientId}");
                throw;
            }
        }

        public async Task<bool> UpdateAsync(OAuthClientModel client)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                var filter = Builders<OAuthClientModel>.Filter.Eq(c => c.ClientId, client.ClientId);
                var result = await collection.ReplaceOneAsync(filter, client);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating OAuth client: {client.ClientId}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string clientId)
        {
            try
            {
                var collection = _database.GetCollection<OAuthClientModel>("oauth_clients");
                var filter = Builders<OAuthClientModel>.Filter.Eq(c => c.ClientId, clientId);
                var result = await collection.DeleteOneAsync(filter);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting OAuth client: {clientId}");
                throw;
            }
        }

        private bool ConstantTimeEquals(string a, string b)
        {
            // Constant-time comparison to prevent timing attacks
            if (a == null || b == null)
                return a == b;

            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= (int)(a[i] ^ b[i]);
            }
            return result == 0;
        }
    }
}

