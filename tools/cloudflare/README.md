# Cloudflare setup scripts

DNS setup for `erp-for-factory.games`. Idempotent + dry-run by default.

## Scripts

| Script | Purpose |
|--------|---------|
| [`Setup-Dns.ps1`](Setup-Dns.ps1) | Configures DNS for the apex + `www` to serve from GitHub Pages, plus zone-level TLS settings. |

## How to run

Two paths:

### Option A — CI (preferred)

Trigger the `Cloudflare DNS setup` workflow manually:

1. GitHub → **Actions** tab → **Cloudflare DNS setup** → **Run workflow**.
2. Leave `apply` = `false` (default) → review the diff in the run log.
3. Re-run with `apply` = `true` once the dry-run looks right.

Token is read from the `CLOUDFLARE_API_TOKEN` repo secret. No local setup needed.

### Option B — Local

1. Create a Cloudflare API token (see "Token scopes" below), set it as
   `CLOUDFLARE_API_TOKEN`:

   ```powershell
   $env:CLOUDFLARE_API_TOKEN = '...'
   ```

2. Dry-run:

   ```powershell
   pwsh tools/cloudflare/Setup-Dns.ps1
   ```

3. Apply when satisfied:

   ```powershell
   pwsh tools/cloudflare/Setup-Dns.ps1 -Apply
   ```

## Token scopes

When creating the token at <https://dash.cloudflare.com/profile/api-tokens>:

- `Zone.Zone:Read`
- `Zone.DNS:Edit`
- `Zone.Zone Settings:Edit`

Zone resource: restrict to `erp-for-factory.games` only.

## What the script does

After it applies, `erp-for-factory.games` serves the GitHub Pages site sourced
from this repo's [`docs/`](../../docs/) folder.

- **Apex** (`erp-for-factory.games`) — four `A` records (IPv4) plus four `AAAA`
  records (IPv6) pointing at GitHub Pages' published addresses
  (`185.199.108-111.153` and `2606:50c0:8000-8003::153`).
  Grey-cloud (proxy off) so GitHub Pages can issue/renew its Let's Encrypt
  certificate without the Cloudflare proxy interfering with the HTTP-01
  challenge.
- **www** — `CNAME` → `chrisonsimtian.github.io`. Same grey-cloud reasoning.
- **Records on apex/www that don't match the desired state get deleted** —
  the script reconciles, not just appends.
- **Zone-level**: `Always-Use-HTTPS = on`, `Minimum TLS = 1.2`.

## What the script deliberately does NOT do

- **Create the GitHub Pages site itself.** Repo Settings → Pages → set source
  to `main` branch `/docs` folder + custom domain `erp-for-factory.games`.
  One-time, two clicks.
- **Add per-game subdomains** (e.g. `satisfactory.erp-for-factory.games`).
  Those get added when the corresponding app ships, per the host convention in
  [ADR-0020](../../docs/adr/0020-rebrand-to-erp-for-factory-games.md).
- **DNSSEC.** Worth doing once nameservers are stable; add as a follow-up.
- **CAA records** pinning Let's Encrypt as the issuer. Hardening step for later.
- **Turn the Cloudflare proxy back on.** Once GH Pages has a stable cert,
  flipping to orange-cloud gives CDN + WAF — known dance, do it manually when
  ready.
