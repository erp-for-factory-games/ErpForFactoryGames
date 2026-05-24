using System.Security.Cryptography;
using System.Text;
using ERP.Application;

namespace ERP.Infrastructure;

/// <summary>
/// SHA-256 <see cref="IAgentTokenHasher"/> (ADR-0025 §2). Stateless;
/// safe to register as a singleton.
/// </summary>
internal sealed class Sha256TokenHasher : IAgentTokenHasher
{
    private const string PlaintextPrefix = "eafg_";
    private const int PlaintextRandomBytes = 32;

    public string MintPlaintext()
    {
        Span<byte> bytes = stackalloc byte[PlaintextRandomBytes];
        RandomNumberGenerator.Fill(bytes);
        var encoded = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return PlaintextPrefix + encoded;
    }

    public byte[] Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
    }
}
