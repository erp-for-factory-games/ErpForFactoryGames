using Erp.Deploy.Cloudflare;
using Microsoft.Extensions.Logging;

namespace Erp.Deploy.Reconcile.Cloudflare;

public sealed class DnsRecordReconciler
{
    private const bool DesiredProxied = true;
    private const int DesiredTtl = 1; // 1 = auto

    private readonly CloudflareClient _cf;
    private readonly ILogger<DnsRecordReconciler> _log;

    public DnsRecordReconciler(CloudflareClient cf, ILogger<DnsRecordReconciler> log)
    {
        _cf = cf;
        _log = log;
    }

    // tunnelIdProvider is a closure because in dry-run with a not-yet-created
    // tunnel we still want to plan the DNS record using a placeholder target,
    // and in apply mode we resolve the real id after the tunnel create.
    public async Task<ResourcePlan> PlanCnameAsync(
        string zoneId,
        string hostname,
        Func<string> tunnelIdProvider,
        bool tunnelWillBeCreated,
        CancellationToken ct)
    {
        string TargetFor() => $"{tunnelIdProvider()}.cfargotunnel.com";

        var existing = (await _cf.FindDnsRecordsAsync(zoneId, hostname, "CNAME", ct)).FirstOrDefault();

        if (existing is null)
        {
            var displayTarget = tunnelWillBeCreated ? "<pending>.cfargotunnel.com" : TargetFor();
            return ResourcePlan.Create(
                "DNS",
                $"CNAME {hostname}",
                new[]
                {
                    FieldChange.Diff("content", null, displayTarget),
                    FieldChange.Diff("proxied", null, DesiredProxied.ToString()),
                    FieldChange.Diff("ttl", null, DesiredTtl.ToString()),
                },
                apply: async c =>
                {
                    var rec = await _cf.CreateCnameAsync(zoneId, hostname, TargetFor(), DesiredProxied, DesiredTtl, c);
                    _log.LogInformation("Created CNAME {Host} → {Target} ({Id})", hostname, rec.Content, rec.Id);
                });
        }

        // Update path — diff each mutable field.
        var desiredContent = tunnelWillBeCreated ? "<pending>.cfargotunnel.com" : TargetFor();
        var changes = new List<FieldChange>
        {
            FieldChange.Diff("content", existing.Content, desiredContent) with { IsChange = !ContentMatches(existing.Content, desiredContent, tunnelWillBeCreated) },
            existing.Proxied == DesiredProxied
                ? FieldChange.Same("proxied", existing.Proxied.ToString())
                : FieldChange.Diff("proxied", existing.Proxied.ToString(), DesiredProxied.ToString()),
            existing.Ttl == DesiredTtl
                ? FieldChange.Same("ttl", existing.Ttl.ToString())
                : FieldChange.Diff("ttl", existing.Ttl.ToString(), DesiredTtl.ToString()),
        };

        if (changes.All(c => !c.IsChange))
        {
            return ResourcePlan.NoOp(
                "DNS",
                $"CNAME {hostname}",
                changes,
                note: $"id={existing.Id}");
        }

        var recordId = existing.Id;
        return ResourcePlan.Update(
            "DNS",
            $"CNAME {hostname}",
            changes,
            apply: async c =>
            {
                var rec = await _cf.UpdateCnameAsync(zoneId, recordId, hostname, TargetFor(), DesiredProxied, DesiredTtl, c);
                _log.LogInformation("Updated CNAME {Host} → {Target} ({Id})", hostname, rec.Content, rec.Id);
            },
            note: $"id={existing.Id}");
    }

    // When the tunnel will be created, the desired target string is a placeholder
    // — we can't compare to existing content meaningfully, so always treat as change.
    private static bool ContentMatches(string existing, string desired, bool tunnelWillBeCreated)
        => !tunnelWillBeCreated && string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase);
}
