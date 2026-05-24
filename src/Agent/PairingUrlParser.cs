namespace Agent;

/// <summary>
/// Parses <c>erp-agent://pair?token=...&amp;api=...</c> deep-link URLs
/// (ADR-0025 §8). Kept separate from <see cref="PairingService"/> so the
/// URL grammar is unit-testable without touching HTTP or the filesystem.
/// </summary>
public static class PairingUrlParser
{
    public const string Scheme = "erp-agent";
    public const string PairHost = "pair";

    /// <summary>
    /// Returns the parsed <see cref="PairingPayload"/> when
    /// <paramref name="url"/> is a well-formed pairing deep-link.
    /// Rejects unknown schemes/hosts, missing parameters, and empty
    /// values — invalid input goes back as <see cref="PairingParseResult.Error"/>
    /// with a human-readable reason.
    /// </summary>
    public static PairingParseResult TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return PairingParseResult.Error("Pairing URL is empty.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return PairingParseResult.Error("Pairing URL is not a well-formed absolute URI.");
        }

        if (!string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return PairingParseResult.Error($"Pairing URL must use the '{Scheme}' scheme; got '{parsed.Scheme}'.");
        }

        if (!string.Equals(parsed.Host, PairHost, StringComparison.OrdinalIgnoreCase))
        {
            return PairingParseResult.Error($"Pairing URL must target '{Scheme}://{PairHost}'; got host '{parsed.Host}'.");
        }

        // System.Web isn't referenced; hand-parse the query so we don't pull
        // another dep into the agent.
        var query = parsed.Query.TrimStart('?');
        string? token = null;
        string? api = null;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = pair.IndexOf('=');
            if (sep < 0) continue;
            var key = Uri.UnescapeDataString(pair[..sep]);
            var value = Uri.UnescapeDataString(pair[(sep + 1)..]);
            if (string.Equals(key, "token", StringComparison.OrdinalIgnoreCase)) token = value;
            else if (string.Equals(key, "api", StringComparison.OrdinalIgnoreCase)) api = value;
        }

        if (string.IsNullOrWhiteSpace(token)) return PairingParseResult.Error("Pairing URL is missing the 'token' parameter.");
        if (string.IsNullOrWhiteSpace(api)) return PairingParseResult.Error("Pairing URL is missing the 'api' parameter.");

        if (!Uri.TryCreate(api, UriKind.Absolute, out var apiUri)
            || (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            return PairingParseResult.Error("Pairing URL 'api' must be an absolute http(s) URL.");
        }

        return PairingParseResult.Success(new PairingPayload(token, apiUri.GetLeftPart(UriPartial.Authority)));
    }
}

/// <summary>The parameters carried by a <c>erp-agent://pair?...</c> URL.</summary>
public readonly record struct PairingPayload(string Token, string ApiBaseUrl);

/// <summary>Outcome of <see cref="PairingUrlParser.TryParse"/>.</summary>
public readonly record struct PairingParseResult(bool IsSuccess, PairingPayload Payload, string? ErrorMessage)
{
    public static PairingParseResult Success(PairingPayload payload) => new(true, payload, null);
    public static PairingParseResult Error(string message) => new(false, default, message);
}
