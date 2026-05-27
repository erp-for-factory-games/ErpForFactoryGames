using Erp.Deploy.Cloudflare;
using Erp.Deploy.Configuration;
using Microsoft.Extensions.Logging;

namespace Erp.Deploy.Reconcile.Cloudflare;

public sealed class IngressReconciler
{
    private readonly CloudflareClient _cf;
    private readonly ILogger<IngressReconciler> _log;

    public IngressReconciler(CloudflareClient cf, ILogger<IngressReconciler> log)
    {
        _cf = cf;
        _log = log;
    }

    public async Task<ResourcePlan> PlanAsync(
        string accountId,
        Func<string> tunnelIdProvider,
        bool tunnelWillBeCreated,
        TunnelSpec spec,
        CancellationToken ct)
    {
        var target = BuildTarget(spec);

        // Skip the GET when the tunnel is brand-new — there's no current config
        // to diff against.
        CfIngressConfig? current = null;
        if (!tunnelWillBeCreated)
        {
            current = await _cf.GetTunnelConfigurationAsync(accountId, tunnelIdProvider(), ct);
        }

        var currentRules = current?.Config.Ingress ?? new List<CfIngressRule>();
        var changes = DiffRules(currentRules, target.Config.Ingress);

        if (!tunnelWillBeCreated && changes.All(c => !c.IsChange))
        {
            return ResourcePlan.NoOp(
                "Ingress",
                spec.Name,
                changes,
                note: $"{target.Config.Ingress.Count} rules, all unchanged");
        }

        return ResourcePlan.Update(
            "Ingress",
            spec.Name,
            changes,
            apply: async c =>
            {
                await _cf.PutTunnelConfigurationAsync(accountId, tunnelIdProvider(), target, c);
                _log.LogInformation("Applied ingress config for tunnel {Name} ({Count} rules)", spec.Name, target.Config.Ingress.Count);
            },
            note: $"{target.Config.Ingress.Count} rules");
    }

    private static CfIngressConfig BuildTarget(TunnelSpec spec)
    {
        var rules = new List<CfIngressRule>();
        foreach (var h in spec.Hostnames)
        {
            rules.Add(new CfIngressRule { Hostname = h.Hostname, Service = h.Service });
        }
        // Catch-all MUST be last. Cloudflare evaluates top-down.
        rules.Add(new CfIngressRule { Service = spec.Catchall });
        return new CfIngressConfig { Config = new CfIngressInner { Ingress = rules } };
    }

    // Positional diff — position matters because Cloudflare evaluates top-down.
    // Comparison key inside a rule is (hostname, path, service).
    private static List<FieldChange> DiffRules(IReadOnlyList<CfIngressRule> current, IReadOnlyList<CfIngressRule> target)
    {
        var max = Math.Max(current.Count, target.Count);
        var changes = new List<FieldChange>(max);
        for (var i = 0; i < max; i++)
        {
            var c = i < current.Count ? current[i] : null;
            var t = i < target.Count ? target[i] : null;
            var name = $"[{i}]";

            if (c is null)
            {
                changes.Add(FieldChange.Diff(name, null, Format(t!)));
            }
            else if (t is null)
            {
                changes.Add(FieldChange.Diff(name, Format(c), null));
            }
            else if (RulesEqual(c, t))
            {
                changes.Add(FieldChange.Same(name, Format(t)));
            }
            else
            {
                changes.Add(FieldChange.Diff(name, Format(c), Format(t)));
            }
        }
        return changes;
    }

    private static bool RulesEqual(CfIngressRule a, CfIngressRule b)
        => string.Equals(a.Hostname ?? "", b.Hostname ?? "", StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Path ?? "", b.Path ?? "", StringComparison.Ordinal)
        && string.Equals(a.Service, b.Service, StringComparison.Ordinal);

    private static string Format(CfIngressRule r)
    {
        if (string.IsNullOrEmpty(r.Hostname))
        {
            return $"* → {r.Service}";
        }
        return string.IsNullOrEmpty(r.Path)
            ? $"{r.Hostname} → {r.Service}"
            : $"{r.Hostname}{r.Path} → {r.Service}";
    }
}
