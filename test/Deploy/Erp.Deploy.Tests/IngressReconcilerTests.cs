using Erp.Deploy.Configuration;
using Erp.Deploy.Reconcile.Cloudflare;

namespace Erp.Deploy.Tests;

/// <summary>
/// Covers the path-aware ingress rule build (ADR-0027 / 5c2 routing split):
/// /api/me → auth-api, /api/* → erp-api, bare hostname → erp-web, catch-all last.
/// Order is load-bearing — Cloudflare evaluates rules top-down.
/// </summary>
public sealed class IngressReconcilerTests
{
    [Fact]
    public void BuildTarget_preserves_order_emits_paths_and_appends_catchall()
    {
        var spec = new TunnelSpec
        {
            Name = "erp",
            Catchall = "http_status:404",
            Hostnames =
            {
                new HostnameSpec { Hostname = "satisfactory.erp-for-factory.games", Path = "/api/me", Service = "http://erp-auth-api:8080" },
                new HostnameSpec { Hostname = "satisfactory.erp-for-factory.games", Path = "/api/.*", Service = "http://erp-api:8080" },
                new HostnameSpec { Hostname = "satisfactory.erp-for-factory.games", Service = "http://erp-web:8080" },
            },
        };

        var rules = IngressReconciler.BuildTarget(spec).Config.Ingress;

        Assert.Equal(4, rules.Count);

        // /api/me must come BEFORE /api/.* or Cloudflare's top-down match would
        // send /api/me to erp-api.
        Assert.Equal("/api/me", rules[0].Path);
        Assert.Equal("http://erp-auth-api:8080", rules[0].Service);

        Assert.Equal("/api/.*", rules[1].Path);
        Assert.Equal("http://erp-api:8080", rules[1].Service);

        // Bare hostname rule carries no path.
        Assert.Null(rules[2].Path);
        Assert.Equal("http://erp-web:8080", rules[2].Service);

        // Catch-all is last and has neither hostname nor path.
        Assert.Null(rules[3].Hostname);
        Assert.Null(rules[3].Path);
        Assert.Equal("http_status:404", rules[3].Service);
    }

    [Fact]
    public void BuildTarget_bare_hostname_stays_pathless()
    {
        var spec = new TunnelSpec
        {
            Name = "erp",
            Hostnames = { new HostnameSpec { Hostname = "x.example", Service = "http://erp-web:8080" } },
        };

        var rules = IngressReconciler.BuildTarget(spec).Config.Ingress;

        Assert.Equal("x.example", rules[0].Hostname);
        Assert.Null(rules[0].Path);
    }
}
