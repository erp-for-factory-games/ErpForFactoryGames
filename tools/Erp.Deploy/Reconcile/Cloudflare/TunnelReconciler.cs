using System.Security.Cryptography;
using Erp.Deploy.Cloudflare;
using Microsoft.Extensions.Logging;

namespace Erp.Deploy.Reconcile.Cloudflare;

public sealed class TunnelReconciler
{
    private readonly CloudflareClient _cf;
    private readonly ILogger<TunnelReconciler> _log;

    public TunnelReconciler(CloudflareClient cf, ILogger<TunnelReconciler> log)
    {
        _cf = cf;
        _log = log;
    }

    public async Task<TunnelReconcileResult> PlanAsync(string accountId, string tunnelName, CancellationToken ct)
    {
        var existing = await _cf.FindTunnelByNameAsync(accountId, tunnelName, ct);
        if (existing.Count > 0)
        {
            var t = existing[0];
            _log.LogInformation("Tunnel {Name} already exists ({Id})", tunnelName, t.Id);
            return new TunnelReconcileResult(
                Name: t.Name,
                Id: t.Id,
                WillCreate: false,
                Plan: ResourcePlan.NoOp(
                    "Tunnel",
                    t.Name,
                    new[] { FieldChange.Same("id", t.Id) },
                    note: "found existing tunnel"),
                ConnectorTokenAsync: c => _cf.GetTunnelConnectorTokenAsync(accountId, t.Id, c));
        }

        // No tunnel — plan a create. The actual create happens in Apply so that
        // dry-run can read this plan without producing side-effects.
        string? createdId = null;

        return new TunnelReconcileResult(
            Name: tunnelName,
            Id: "<pending>",
            WillCreate: true,
            Plan: ResourcePlan.Create(
                "Tunnel",
                tunnelName,
                new[]
                {
                    FieldChange.Diff("name", null, tunnelName),
                    FieldChange.Diff("config_src", null, "cloudflare"),
                },
                apply: async c =>
                {
                    var secret = GenerateTunnelSecret();
                    var created = await _cf.CreateTunnelAsync(accountId, tunnelName, secret, c);
                    createdId = created.Id;
                    _log.LogInformation("Created tunnel {Name} ({Id})", tunnelName, created.Id);
                },
                note: "tunnel_secret generated at apply-time"),
            ConnectorTokenAsync: async c =>
            {
                if (createdId is null)
                {
                    throw new InvalidOperationException("Cannot fetch connector token before tunnel create is applied.");
                }
                return await _cf.GetTunnelConnectorTokenAsync(accountId, createdId, c);
            });
    }

    private static string GenerateTunnelSecret()
    {
        // 32 random bytes, base64-encoded. Token-based connectors don't use
        // this value but the create endpoint demands it as a required field.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
