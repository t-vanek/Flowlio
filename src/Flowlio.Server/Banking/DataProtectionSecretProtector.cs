using Flowlio.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Flowlio.Server.Banking;

/// <summary>
/// Protects sensitive values with ASP.NET Data Protection. The key ring is persisted to Redis (see
/// <c>Program.cs</c>), so encrypted secrets such as a user's Open Banking private key survive restarts and
/// are decryptable across instances. The purpose string isolates this protector from cookie/antiforgery keys.
/// </summary>
internal sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("Flowlio.EnableBanking.PrivateKey.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
