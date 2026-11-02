using Blocks.Genesis.Entities.Iam;
using Mfa.DomainService.Services;

namespace Mfa.DomainService.TOTP
{
    /// <summary>Removes a user's TOTP rows together with their secrets in Blocks Secrets.</summary>
    public static class TotpSecretRows
    {
        /// <summary>Retires every stored TOTP secret of the user in Blocks Secrets, then deletes the rows.</summary>
        public static async Task DeleteAsync(IMfaManagementRepository repository, ITotpSecretResolver secrets, string userId)
        {
            var rows = await repository.GetItemsAsync<UserTotpDetail>(t => t.CreatedBy == userId) ?? [];
            foreach (var row in rows)
            {
                await secrets.RetireAsync(userId, row.SecretReference);
            }

            await repository.DeleteItemsAsync<UserTotpDetail>(t => t.CreatedBy == userId);
        }
    }
}
