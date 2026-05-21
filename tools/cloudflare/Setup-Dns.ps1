<#
.SYNOPSIS
    Configure Cloudflare DNS for erp-for-factory.games to serve from GitHub Pages.

.DESCRIPTION
    Idempotent setup script for the project's apex domain on Cloudflare. After
    this runs, https://erp-for-factory.games and https://www.erp-for-factory.games
    serve the GitHub Pages site sourced from this repo's `/docs/` folder.

    DNS topology:
      - Apex (erp-for-factory.games) — four A records pointing at GitHub Pages'
        published IPs. Grey-cloud (proxy off) so GitHub Pages handles TLS via
        Let's Encrypt directly. Re-enable orange-cloud later if you want
        Cloudflare's CDN/WAF in front.
      - www — CNAME to <user>.github.io (same grey-cloud reasoning).
      - Per-game subdomains (e.g. satisfactory.erp-for-factory.games) are NOT
        created here — they go in when their app is actually deployed. The host
        convention is documented in ADR-0020.

    Zone settings:
      - Always-Use-HTTPS = on
      - Minimum TLS = 1.2

    Defaults to **dry-run**. Pass -Apply to actually mutate Cloudflare.

.PARAMETER Zone
    Domain registered on Cloudflare. Default: erp-for-factory.games.

.PARAMETER GhPagesHost
    Hostname for the www CNAME — your GitHub Pages canonical host
    (`<user>.github.io`). Default: chrisonsimtian.github.io.

.PARAMETER Apply
    Actually call the Cloudflare API. Without this, the script only prints what
    *would* be done. Always run once without -Apply first.

.NOTES
    Requires the CLOUDFLARE_API_TOKEN env var with permissions:
      - Zone.Zone:Read
      - Zone.DNS:Edit
      - Zone.Zone Settings:Edit

    In CI this comes from the repo secret of the same name (see
    .github/workflows/cloudflare-dns.yml).

    Re-running with -Apply is safe: every operation checks existing state first
    and only creates/updates/deletes what's needed to converge on the desired
    state. Records on apex / www that don't match the desired set are removed.

.EXAMPLE
    # 1. Dry run locally — see what would happen
    $env:CLOUDFLARE_API_TOKEN = '...'
    pwsh tools/cloudflare/Setup-Dns.ps1

    # 2. Apply
    pwsh tools/cloudflare/Setup-Dns.ps1 -Apply

    # 3. CI: trigger the cloudflare-dns workflow manually with apply=true.
#>

[CmdletBinding()]
param(
    [string]$Zone = 'erp-for-factory.games',
    [string]$GhPagesHost = 'chrisonsimtian.github.io',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

# GitHub Pages' published apex addresses (both v4 and v6).
# https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site#configuring-an-apex-domain
$GhPagesIps = @(
    '185.199.108.153',
    '185.199.109.153',
    '185.199.110.153',
    '185.199.111.153'
)
$GhPagesIpv6s = @(
    '2606:50c0:8000::153',
    '2606:50c0:8001::153',
    '2606:50c0:8002::153',
    '2606:50c0:8003::153'
)

# ---------------------------------------------------------------------------
# Auth + helpers
# ---------------------------------------------------------------------------

if (-not $env:CLOUDFLARE_API_TOKEN) {
    throw "CLOUDFLARE_API_TOKEN env var not set. In CI this comes from secrets.CLOUDFLARE_API_TOKEN; locally create a scoped token at https://dash.cloudflare.com/profile/api-tokens"
}

$headers = @{
    Authorization  = "Bearer $env:CLOUDFLARE_API_TOKEN"
    'Content-Type' = 'application/json'
}
$base = 'https://api.cloudflare.com/client/v4'

function Invoke-Cf {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body
    )
    $invokeArgs = @{
        Method  = $Method
        Uri     = "$base$Path"
        Headers = $headers
    }
    if ($Body) {
        $invokeArgs.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }
    Invoke-RestMethod @invokeArgs
}

function Step {
    param([string]$Message, [string]$Mode = 'INFO')
    $colour = switch ($Mode) {
        'WOULD'   { 'Yellow' }
        'APPLIED' { 'Green' }
        'SKIP'    { 'DarkGray' }
        'DELETE'  { 'Magenta' }
        default   { 'Cyan' }
    }
    Write-Host "[$Mode] $Message" -ForegroundColor $colour
}

# ---------------------------------------------------------------------------
# 0. Look up zone
# ---------------------------------------------------------------------------

Step "Looking up zone '$Zone'"
$zoneResult = Invoke-Cf -Method GET -Path "/zones?name=$Zone"
$zoneInfo = $zoneResult.result | Select-Object -First 1
if (-not $zoneInfo) {
    throw "Zone '$Zone' not found on this Cloudflare account. Is the API token scoped to it?"
}
$zoneId = $zoneInfo.id
Step "Zone ID: $zoneId  (status: $($zoneInfo.status))"

if ($zoneInfo.status -ne 'active') {
    Write-Warning "Zone is not 'active' yet (status: $($zoneInfo.status)). Nameservers may still be propagating; you can continue but Cloudflare won't serve traffic until status flips to active."
}

# ---------------------------------------------------------------------------
# 1. Build desired DNS state
#
#    Apex: 4× A records pointing at GitHub Pages, grey-cloud (proxied=$false).
#          Grey-cloud lets GH Pages issue/renew its own Let's Encrypt cert
#          without the Cloudflare proxy interfering with the HTTP-01 challenge.
#    www:  CNAME → <user>.github.io, also grey-cloud.
#
#    No satisfactory.<apex> record — that's added when the Satisfactory app
#    actually ships (per ADR-0020's host convention).
# ---------------------------------------------------------------------------

$desired = @()
foreach ($ip in $GhPagesIps) {
    $desired += @{ type = 'A'; name = $Zone; content = $ip; proxied = $false; comment = 'GitHub Pages — apex (IPv4)' }
}
foreach ($ip in $GhPagesIpv6s) {
    $desired += @{ type = 'AAAA'; name = $Zone; content = $ip; proxied = $false; comment = 'GitHub Pages — apex (IPv6)' }
}
$desired += @{ type = 'CNAME'; name = "www.$Zone"; content = $GhPagesHost; proxied = $false; comment = 'GitHub Pages — www' }

# Names we manage. Records under these names that don't match `$desired` get
# removed (idempotency). Records under OTHER names are left alone.
$managedNames = @($Zone, "www.$Zone")

# ---------------------------------------------------------------------------
# 2. Reconcile DNS records
# ---------------------------------------------------------------------------

$existingRecords = (Invoke-Cf -Method GET -Path "/zones/$zoneId/dns_records?per_page=100").result

function Test-RecordMatch {
    param($Existing, $Desired)
    return ($Existing.type -eq $Desired.type) `
        -and ($Existing.name -eq $Desired.name) `
        -and ($Existing.content -eq $Desired.content) `
        -and ($Existing.proxied -eq $Desired.proxied)
}

# 2a. Remove records under managed names that aren't in $desired.
foreach ($existing in $existingRecords) {
    if ($existing.name -notin $managedNames) { continue }
    $stillWanted = $desired | Where-Object { Test-RecordMatch -Existing $existing -Desired $_ }
    if ($stillWanted) { continue }

    if ($Apply) {
        Invoke-Cf -Method DELETE -Path "/zones/$zoneId/dns_records/$($existing.id)" | Out-Null
        Step "$($existing.name) ($($existing.type) → $($existing.content)) — deleted" 'DELETE'
    } else {
        Step "$($existing.name) ($($existing.type) → $($existing.content)) — would DELETE (proxied=$($existing.proxied))" 'WOULD'
    }
}

# 2b. Create records in $desired that aren't already present.
$existingRecords = (Invoke-Cf -Method GET -Path "/zones/$zoneId/dns_records?per_page=100").result
foreach ($d in $desired) {
    $alreadyThere = $existingRecords | Where-Object { Test-RecordMatch -Existing $_ -Desired $d }
    if ($alreadyThere) {
        Step "$($d.name) ($($d.type) → $($d.content)) — already correct" 'SKIP'
        continue
    }

    if ($Apply) {
        Invoke-Cf -Method POST -Path "/zones/$zoneId/dns_records" -Body $d | Out-Null
        Step "$($d.name) ($($d.type) → $($d.content)) — created (proxied=$($d.proxied))" 'APPLIED'
    } else {
        Step "$($d.name) ($($d.type) → $($d.content)) — would CREATE (proxied=$($d.proxied))" 'WOULD'
    }
}

# ---------------------------------------------------------------------------
# 3. Zone settings: Always-Use-HTTPS, Minimum TLS 1.2
# ---------------------------------------------------------------------------

$desiredSettings = @(
    @{ id = 'always_use_https'; value = 'on';  label = "Always-Use-HTTPS = on" },
    @{ id = 'min_tls_version';  value = '1.2'; label = "Minimum TLS = 1.2" }
)

foreach ($s in $desiredSettings) {
    $current = (Invoke-Cf -Method GET -Path "/zones/$zoneId/settings/$($s.id)").result.value
    if ($current -eq $s.value) {
        Step "$($s.label) — already set" 'SKIP'
        continue
    }
    if ($Apply) {
        Invoke-Cf -Method PATCH -Path "/zones/$zoneId/settings/$($s.id)" -Body @{ value = $s.value } | Out-Null
        Step "$($s.label) — applied (was: $current)" 'APPLIED'
    } else {
        Step "$($s.label) — would SET (currently: $current)" 'WOULD'
    }
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry run complete. Re-run with -Apply to mutate Cloudflare." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "Done. Next steps:" -ForegroundColor Green
    Write-Host "  1. In the GitHub repo: Settings → Pages → set Custom domain to '$Zone' (if not already)."
    Write-Host "  2. Wait for the GH Pages 'DNS check' to go green (can take a few minutes)."
    Write-Host "  3. Enable 'Enforce HTTPS' in the Pages settings once the cert is issued."
    Write-Host ""
    Write-Host "Verify:" -ForegroundColor Green
    Write-Host "  dig +short $Zone        # expect: 185.199.108.153 (and three siblings)"
    Write-Host "  dig +short www.$Zone    # expect: $GhPagesHost  → GH Pages IPs"
    Write-Host "  curl -sI https://$Zone  # expect: 200 from GitHub Pages"
}
