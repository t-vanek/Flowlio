namespace Flowlio.Application.Abstractions;

/// <summary>
/// Encrypts and decrypts small sensitive values (e.g. a family's Open Banking private key) for storage at
/// rest. Implemented over ASP.NET Data Protection, whose key ring is persisted to Redis, so protected values
/// survive restarts and are decryptable across instances.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
