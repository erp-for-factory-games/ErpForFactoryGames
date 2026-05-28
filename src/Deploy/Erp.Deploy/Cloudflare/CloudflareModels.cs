using System.Text.Json.Serialization;

namespace Erp.Deploy.Cloudflare;

// Cloudflare's API wraps every response in { success, errors, messages, result }.
// We project `result` into a typed `T` and surface success/errors at the boundary.

internal sealed class CfEnvelope<T>
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("errors")] public List<CfMessage>? Errors { get; set; }
    [JsonPropertyName("messages")] public List<CfMessage>? Messages { get; set; }
    [JsonPropertyName("result")] public T? Result { get; set; }
}

internal sealed class CfMessage
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

public sealed class CfAccount
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public sealed class CfZone
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public sealed class CfTunnel
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("config_src")] public string? ConfigSrc { get; set; }
    [JsonPropertyName("deleted_at")] public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class CfTunnelCreateBody
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("tunnel_secret")] public string TunnelSecret { get; set; } = string.Empty;
    [JsonPropertyName("config_src")] public string ConfigSrc { get; set; } = "cloudflare";
}

public sealed class CfIngressConfig
{
    [JsonPropertyName("config")] public CfIngressInner Config { get; set; } = new();
}

public sealed class CfIngressInner
{
    [JsonPropertyName("ingress")] public List<CfIngressRule> Ingress { get; set; } = new();
}

public sealed class CfIngressRule
{
    [JsonPropertyName("hostname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hostname { get; set; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonPropertyName("service")] public string Service { get; set; } = string.Empty;
}

public sealed class CfDnsRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("proxied")] public bool Proxied { get; set; }
    [JsonPropertyName("ttl")] public int Ttl { get; set; }
}

internal sealed class CfDnsRecordUpsertBody
{
    [JsonPropertyName("type")] public string Type { get; set; } = "CNAME";
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("proxied")] public bool Proxied { get; set; } = true;
    [JsonPropertyName("ttl")] public int Ttl { get; set; } = 1;
}
