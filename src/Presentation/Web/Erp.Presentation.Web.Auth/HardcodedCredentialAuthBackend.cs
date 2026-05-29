using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Erp.Presentation.Web.Auth;

/// <summary>
/// <see cref="IAuthBackend"/> backed by a single configured username/password
/// (<c>Auth:Hardcoded:*</c>). The zero-infrastructure default so the app runs
/// without standing up Keycloak (ADR-0028 §7). Comparison is constant-time
/// (SHA-256 then fixed-time compare) so it doesn't leak the secret by timing.
/// </summary>
public sealed class HardcodedCredentialAuthBackend : IAuthBackend
{
    private readonly AuthLandingOptions.HardcodedCredentials _creds;

    public HardcodedCredentialAuthBackend(IOptions<AuthLandingOptions> options)
        => _creds = options.Value.Hardcoded;

    public string Name => "hardcoded";

    public Task<AuthBackendResult> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        // Evaluate both halves regardless of the first so the work — and thus
        // the timing — doesn't depend on whether the username matched.
        var userOk = ConstantTimeEquals(username, _creds.Username);
        var passOk = ConstantTimeEquals(password, _creds.Password);

        // Refuse to authenticate against an empty configured password — that
        // would let anyone in if the secret was never set.
        var ok = userOk & passOk && !string.IsNullOrEmpty(_creds.Password);

        return Task.FromResult(ok
            ? AuthBackendResult.Ok(subject: $"local:{_creds.Username}", _creds.DisplayName)
            : AuthBackendResult.Fail());
    }

    // Hash both sides to a fixed 32-byte digest first so FixedTimeEquals gets
    // equal-length inputs and the comparison doesn't leak length either.
    private static bool ConstantTimeEquals(string? a, string? b)
    {
        Span<byte> ha = stackalloc byte[32];
        Span<byte> hb = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(a ?? string.Empty), ha);
        SHA256.HashData(Encoding.UTF8.GetBytes(b ?? string.Empty), hb);
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }
}
