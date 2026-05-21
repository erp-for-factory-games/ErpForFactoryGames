# Cloudflare setup scripts

One-time DNS and redirect setup for `erp-for-factory.games`.

## Scripts

| Script | Purpose |
|--------|---------|
| [`Setup-Dns.ps1`](Setup-Dns.ps1) | Configures DNS records, the apex/www → GitHub redirect rule, and TLS zone settings. Idempotent + dry-run by default. |

## One-time setup

1. **Create a scoped API token** at <https://dash.cloudflare.com/profile/api-tokens>.
   Permissions:
   - `Zone.Zone:Read`
   - `Zone.DNS:Edit`
   - `Zone.Zone Settings:Edit`
   - `Zone.Rulesets:Edit` (for Single Redirects)

   Restrict the token to the `erp-for-factory.games` zone only.

2. **Dry-run** the setup script to see what would change:

   ```powershell
   $env:CLOUDFLARE_API_TOKEN = '...'
   pwsh tools/cloudflare/Setup-Dns.ps1
   ```

3. **Apply** when satisfied:

   ```powershell
   pwsh tools/cloudflare/Setup-Dns.ps1 -Apply
   ```

4. **Verify**:

   ```powershell
   curl.exe -sI https://erp-for-factory.games        # 301 -> GitHub repo
   curl.exe -sI https://www.erp-for-factory.games    # 301 -> GitHub repo
   ```

## What it does

- Creates proxied `A` records on `erp-for-factory.games`, `www.erp-for-factory.games`,
  and `satisfactory.erp-for-factory.games` pointing at `192.0.2.1` (TEST-NET-1).
  The redirect fires at the Cloudflare edge, so traffic never reaches the IP.
- Creates a Single Redirect rule: apex + www → the GitHub repo (until the app
  has a hosted home).
- `satisfactory.erp-for-factory.games` is reserved with a proxied placeholder
  record only — no redirect rule yet. Locks in the host convention from
  [ADR-0020](../../docs/adr/0020-rebrand-to-erp-for-factory-games.md) without
  serving anything until the app is deployed.
- Sets `Always-Use-HTTPS = on` and `Minimum TLS = 1.2` on the zone.

## What it deliberately does NOT do

- **Deploy the app.** Hosting choice (GitHub Pages / Azure SWA / Cloudflare Pages
  / Fly.io / Proxmox homelab) is out of scope until the rebrand milestone is
  closed and ADR-0020's "follow-up" issues are picked up.
- **Configure DNSSEC.** Worth doing once nameservers are stable; add as a
  follow-up.
- **Add per-game subdomains beyond `satisfactory.*`.** Done case-by-case as
  each game lands.
- **CAA records.** Cloudflare provides certificates via Universal SSL — adding
  CAA pinned to Cloudflare's issuers is a hardening step for later.
