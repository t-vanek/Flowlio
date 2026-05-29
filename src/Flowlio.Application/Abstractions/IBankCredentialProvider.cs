namespace Flowlio.Application.Abstractions;

/// <summary>
/// Resolves a family's stored Open Banking credentials, decrypting the private key for use. Each family
/// brings its own Enable Banking application, so credentials are looked up per family rather than from a
/// single server-wide configuration.
/// </summary>
public interface IBankCredentialProvider
{
    /// <summary>The family's credentials with the private key decrypted, or null when none are stored.</summary>
    Task<BankProviderCredentials?> GetAsync(Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>Whether the family has Open Banking credentials stored (without decrypting the key).</summary>
    Task<bool> HasAsync(Guid familyId, CancellationToken cancellationToken = default);
}
