namespace Erp.Deploy.Configuration;

public sealed class DeployOptions
{
    public CloudflareOptions Cloudflare { get; init; } = new();
    public RemoteOptions Remote { get; init; } = new();
}

public sealed class CloudflareOptions
{
    public string Zone { get; init; } = string.Empty;
    public List<TunnelSpec> Tunnels { get; init; } = new();
}

public sealed class TunnelSpec
{
    public string Name { get; init; } = string.Empty;
    public List<HostnameSpec> Hostnames { get; init; } = new();
    public string Catchall { get; init; } = "http_status:404";
}

public sealed class HostnameSpec
{
    public string Hostname { get; init; } = string.Empty;

    /// <summary>
    /// Optional Cloudflare ingress path matcher (regex), e.g. <c>/api/me</c>.
    /// When set the rule only matches that path on the hostname — used to split
    /// <c>/api/me</c> (→ auth-api) from the rest of <c>/api/*</c> (→ erp-api)
    /// per ADR-0027 / 5c2. Null matches the whole hostname. Order matters: list
    /// more-specific path rules BEFORE the bare-hostname rule (Cloudflare
    /// evaluates top-down).
    /// </summary>
    public string? Path { get; init; }

    public string Service { get; init; } = string.Empty;
}

public sealed class RemoteOptions
{
    // Host is the SSH config alias OR a raw hostname. SshConnectionResolver
    // shells out to `ssh -G <Host>` and picks up Hostname/Port/User/IdentityFile
    // from ~/.ssh/config. Explicit values below override what ssh -G resolves.
    public string Host { get; init; } = "erp-lxc";

    // Optional overrides. When null/0, the value resolved from `ssh -G` wins.
    public int? Port { get; init; }
    public string? User { get; init; }
    public string? IdentityFile { get; init; }

    public string StackDir { get; init; } = "/home/chris/stacks/erp";
}
