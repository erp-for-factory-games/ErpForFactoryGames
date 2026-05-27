using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;

namespace Erp.Deploy.Cloudflare;

// Thin typed client over Cloudflare's v4 REST API. Seven endpoints, all the
// ones provision.ps1 hits. Bearer-token auth via ICloudflareTokenSource so the
// token never gets baked into options/config and dependent code can mock it.
public sealed class CloudflareClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<CloudflareClient> _log;
    private readonly ICloudflareTokenSource _tokenSource;

    public CloudflareClient(HttpClient http, ICloudflareTokenSource tokenSource, ILogger<CloudflareClient> log)
    {
        _http = http;
        _tokenSource = tokenSource;
        _log = log;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        }
    }

    public async Task<IReadOnlyList<CfAccount>> ListAccountsAsync(CancellationToken ct = default)
        => await GetListAsync<CfAccount>("accounts", ct);

    public async Task<IReadOnlyList<CfZone>> FindZoneByNameAsync(string name, CancellationToken ct = default)
        => await GetListAsync<CfZone>($"zones?name={HttpUtility.UrlEncode(name)}", ct);

    public async Task<IReadOnlyList<CfTunnel>> FindTunnelByNameAsync(string accountId, string name, CancellationToken ct = default)
        => await GetListAsync<CfTunnel>(
            $"accounts/{accountId}/cfd_tunnel?name={HttpUtility.UrlEncode(name)}&is_deleted=false",
            ct);

    public async Task<CfTunnel> CreateTunnelAsync(string accountId, string name, string tunnelSecret, CancellationToken ct = default)
    {
        var body = new CfTunnelCreateBody { Name = name, TunnelSecret = tunnelSecret, ConfigSrc = "cloudflare" };
        return await PostAsync<CfTunnelCreateBody, CfTunnel>($"accounts/{accountId}/cfd_tunnel", body, ct);
    }

    // Cloudflare returns the connector token as a bare JSON string in the `result` field.
    public async Task<string> GetTunnelConnectorTokenAsync(string accountId, string tunnelId, CancellationToken ct = default)
        => await GetAsync<string>($"accounts/{accountId}/cfd_tunnel/{tunnelId}/token", ct);

    public async Task<CfIngressConfig?> GetTunnelConfigurationAsync(string accountId, string tunnelId, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<CfIngressConfig>($"accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations", ct);
        }
        catch (CloudflareApiException ex) when (ex.HttpStatus == 404)
        {
            return null;
        }
    }

    public async Task PutTunnelConfigurationAsync(string accountId, string tunnelId, CfIngressConfig config, CancellationToken ct = default)
        => await PutAsync<CfIngressConfig, object>($"accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations", config, ct);

    public async Task<IReadOnlyList<CfDnsRecord>> FindDnsRecordsAsync(string zoneId, string name, string type, CancellationToken ct = default)
        => await GetListAsync<CfDnsRecord>(
            $"zones/{zoneId}/dns_records?name={HttpUtility.UrlEncode(name)}&type={HttpUtility.UrlEncode(type)}",
            ct);

    public async Task<CfDnsRecord> CreateCnameAsync(string zoneId, string name, string content, bool proxied, int ttl, CancellationToken ct = default)
    {
        var body = new CfDnsRecordUpsertBody { Type = "CNAME", Name = name, Content = content, Proxied = proxied, Ttl = ttl };
        return await PostAsync<CfDnsRecordUpsertBody, CfDnsRecord>($"zones/{zoneId}/dns_records", body, ct);
    }

    public async Task<CfDnsRecord> UpdateCnameAsync(string zoneId, string recordId, string name, string content, bool proxied, int ttl, CancellationToken ct = default)
    {
        var body = new CfDnsRecordUpsertBody { Type = "CNAME", Name = name, Content = content, Proxied = proxied, Ttl = ttl };
        return await PutAsync<CfDnsRecordUpsertBody, CfDnsRecord>($"zones/{zoneId}/dns_records/{recordId}", body, ct);
    }

    // ----- Verb helpers --------------------------------------------------

    private Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<object, T>(HttpMethod.Get, path, null, ct);
    private Task<TOut> PostAsync<TIn, TOut>(string path, TIn body, CancellationToken ct) => SendAsync<TIn, TOut>(HttpMethod.Post, path, body, ct);
    private Task<TOut> PutAsync<TIn, TOut>(string path, TIn body, CancellationToken ct) => SendAsync<TIn, TOut>(HttpMethod.Put, path, body, ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        var list = await SendAsync<object, List<T>>(HttpMethod.Get, path, null, ct);
        return list ?? new List<T>();
    }

    private async Task<TOut> SendAsync<TIn, TOut>(HttpMethod method, string path, TIn? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenSource.GetToken());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null && method != HttpMethod.Get)
        {
            req.Content = JsonContent.Create(body, options: Json);
        }

        _log.LogDebug("cf {Method} {Path}", method.Method, path);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var status = (int)resp.StatusCode;
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new CloudflareApiException(method.Method, path, status, Array.Empty<(int, string)>(), $"empty response body (HTTP {status})");
        }

        CfEnvelope<TOut>? env;
        try
        {
            env = JsonSerializer.Deserialize<CfEnvelope<TOut>>(raw, Json);
        }
        catch (JsonException jex)
        {
            throw new CloudflareApiException(method.Method, path, status, Array.Empty<(int, string)>(),
                $"non-JSON response (HTTP {status}): {Truncate(raw, 200)}", jex);
        }

        if (env is null || !env.Success)
        {
            var errs = (env?.Errors ?? new List<CfMessage>())
                .Select(e => (e.Code, e.Message))
                .ToList();
            throw new CloudflareApiException(method.Method, path, status, errs,
                $"Cloudflare API {method.Method} {path} failed (HTTP {status}): " +
                (errs.Count > 0 ? string.Join("; ", errs.Select(e => $"[{e.Code}] {e.Message}")) : "no error details"));
        }

        return env.Result!;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
