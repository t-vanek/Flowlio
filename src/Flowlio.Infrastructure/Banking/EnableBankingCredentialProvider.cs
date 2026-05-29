using Flowlio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Infrastructure.Banking;

/// <summary>Loads a user's stored Enable Banking credentials and decrypts the private key for use.</summary>
internal sealed class EnableBankingCredentialProvider(IAppDbContext db, ISecretProtector protector)
    : IBankCredentialProvider
{
    public async Task<BankProviderCredentials?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var credential = await db.EnableBankingCredentials
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (credential is null)
            return null;

        var privateKey = protector.Unprotect(credential.PrivateKeyEncrypted);
        return new BankProviderCredentials(credential.ApplicationId, privateKey);
    }

    public Task<bool> HasAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.EnableBankingCredentials.AnyAsync(c => c.UserId == userId, cancellationToken);
}
