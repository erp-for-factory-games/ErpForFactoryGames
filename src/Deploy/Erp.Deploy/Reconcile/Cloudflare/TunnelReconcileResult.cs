namespace Erp.Deploy.Reconcile.Cloudflare;

// Output of TunnelReconciler. The Id is what every downstream resource keys
// off (DNS CNAME target, ingress PUT path). ConnectorTokenAsync fetches the
// token lazily — we don't want to GET it in dry-run mode.
public sealed record TunnelReconcileResult(
    string Name,
    string Id,
    bool WillCreate,
    ResourcePlan Plan,
    Func<CancellationToken, Task<string>> ConnectorTokenAsync);
