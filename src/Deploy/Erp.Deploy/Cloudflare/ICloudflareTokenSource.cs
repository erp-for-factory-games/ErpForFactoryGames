namespace Erp.Deploy.Cloudflare;

// Source of the Cloudflare API bearer token. The library doesn't care where
// the secret came from — env var, prompt, Bitwarden, GitHub Actions secret —
// just that the caller can produce one.
public interface ICloudflareTokenSource
{
    string GetToken();
}

public sealed class InlineCloudflareTokenSource : ICloudflareTokenSource
{
    private readonly string _token;

    public InlineCloudflareTokenSource(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Cloudflare API token must be a non-empty string.", nameof(token));
        }
        _token = token;
    }

    public string GetToken() => _token;
}
