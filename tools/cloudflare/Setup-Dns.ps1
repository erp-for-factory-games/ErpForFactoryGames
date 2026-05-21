<#
.SYNOPSIS
    Configure Cloudflare DNS and redirects for erp-for-factory.games.

.DESCRIPTION
    Idempotent setup script for the project's apex domain on Cloudflare. Sets up:

      1. A "parking" A record on apex + www, proxied through Cloudflare so a
         redirect rule has DNS to attach to. Points at 192.0.2.1 (TEST-NET-1) —
         the redirect fires at the Cloudflare edge, traffic never reaches the IP.
      2. A "placeholder" A record on satisfactory.<apex>, also proxied.
         Reserves the host convention from ADR-0020 without serving anything yet.
      3. A Single Redirect rule: apex + www → the GitHub repo (until the app
         has a hosted home).
      4. Zone settings: Always-Use-HTTPS = on, Minimum TLS = 1.2.

    Defaults to **dry-run**. Pass -Apply to actually mutate Cloudflare.

.PARAMETER Zone
    Domain registered on Cloudflare. Default: erp-for-factory.games.

.PARAMETER RedirectTarget
    URL the apex (and www) redirects to until the app has a hosted home.
    Default: https://github.com/ChrisonSimtian/ErpForFactoryGames.

.PARAMETER Apply
    Actually call the Cloudflare API. Without this, the script only prints what
    *would* be done. Always run once without -Apply first.

.PARAMETER PlaceholderIp
    IPv4 address used for the proxied parking records. TEST-NET-1 (192.0.2.1)
    is reserved for documentation and won't route — perfect for "DNS exists so
    Cloudflare proxies, but nothing's actually hosted there yet".

.NOTES
    Requires the CLOUDFLARE_API_TOKEN env var with permissions:
      - Zone.Zone:Read
      - Zone.DNS:Edit
      - Zone.Zone Settings:Edit
      - Zone.Page Rules / Redirect Rules:Edit (Account level: Bulk URL Redirects:Edit)

    Create a scoped token at:
      https://dash.cloudflare.com/profile/api-tokens
      → "Create Token" → "Edit zone DNS" template → restrict to this zone.

    Re-running with -Apply is safe: every operation checks existing state first.

.EXAMPLE
    # 1. Dry run — see what would happen
    pwsh tools/cloudflare/Setup-Dns.ps1

    # 2. Apply for real
    $env:CLOUDFLARE_API_TOKEN = '...'
    pwsh tools/cloudflare/Setup-Dns.ps1 -Apply
#>

[CmdletBinding()]
param(
    [string]$Zone = 'erp-for-factory.games',
    [string]$RedirectTarget = 'https://github.com/ChrisonSimtian/ErpForFactoryGames',
    [switch]$Apply,
    [string]$PlaceholderIp = '192.0.2.1'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Auth + helpers
# ---------------------------------------------------------------------------

if (-not $env:CLOUDFLARE_API_TOKEN) {
    throw "CLOUDFLARE_API_TOKEN env var not set. Create a scoped token at https://dash.cloudflare.com/profile/api-tokens"
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
    $args = @{
        Method  = $Method
        Uri     = "$base$Path"
        Headers = $headers
    }
    if ($Body) {
        $args.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }
    Invoke-RestMethod @args
}

function Step {
    param([string]$Message, [string]$Mode = 'INFO')
    $colour = switch ($Mode) {
        'WOULD'   { 'Yellow' }
        'APPLIED' { 'Green' }
        'SKIP'    { 'DarkGray' }
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
Step "Zone ID: $zoneId  (status: $($zoneInfo.status), nameservers: $($zoneInfo.name_servers -join ', '))"

if ($zoneInfo.status -ne 'active') {
    Write-Warning "Zone is not 'active' yet (status: $($zoneInfo.status)). Nameservers may still be propagating; you can continue but Cloudflare won't serve traffic until status flips to active."
}

# ---------------------------------------------------------------------------
# 1. DNS records — apex, www, satisfactory.<apex>
#    Proxied parking records pointing at TEST-NET-1.
# ---------------------------------------------------------------------------

$existingRecords = (Invoke-Cf -Method GET -Path "/zones/$zoneId/dns_records?per_page=100").result

$desiredRecords = @(
    @{ type = 'A'; name = $Zone;                      content = $PlaceholderIp; proxied = $true; comment = 'Apex parking — redirect rule attached' },
    @{ type = 'A'; name = "www.$Zone";                content = $PlaceholderIp; proxied = $true; comment = 'www → apex via redirect rule' },
    @{ type = 'A'; name = "satisfactory.$Zone";       content = $PlaceholderIp; proxied = $true; comment = 'Placeholder — host convention per ADR-0020. App not yet deployed.' }
)

foreach ($desired in $desiredRecords) {
    $existing = $existingRecords | Where-Object { $_.type -eq $desired.type -and $_.name -eq $desired.name }
    if ($existing) {
        if ($existing.content -eq $desired.content -and $existing.proxied -eq $desired.proxied) {
            Step "$($desired.name) ($($desired.type)) already correct — skip" 'SKIP'
            continue
        }
        if ($Apply) {
            Invoke-Cf -Method PUT -Path "/zones/$zoneId/dns_records/$($existing.id)" -Body $desired | Out-Null
            Step "$($desired.name) ($($desired.type)) → updated" 'APPLIED'
        } else {
            Step "$($desired.name) ($($desired.type)) → would UPDATE to $($desired.content) (proxied=$($desired.proxied))" 'WOULD'
        }
    } else {
        if ($Apply) {
            Invoke-Cf -Method POST -Path "/zones/$zoneId/dns_records" -Body $desired | Out-Null
            Step "$($desired.name) ($($desired.type)) → created" 'APPLIED'
        } else {
            Step "$($desired.name) ($($desired.type)) → would CREATE pointing at $($desired.content) (proxied=$($desired.proxied))" 'WOULD'
        }
    }
}

# ---------------------------------------------------------------------------
# 2. Single Redirect rule: apex + www → RedirectTarget
#    Uses the http_request_dynamic_redirect phase ruleset (Cloudflare's
#    modern "Single Redirects" feature). Falls back to creating the ruleset
#    if it doesn't exist yet.
# ---------------------------------------------------------------------------

$ruleDescription = 'Apex + www → GitHub repo (until app hosted)'
$ruleExpression  = "(http.host eq `"$Zone`") or (http.host eq `"www.$Zone`")"
$ruleAction = @{
    action            = 'redirect'
    action_parameters = @{
        from_value = @{
            status_code           = 301
            target_url            = @{ value = $RedirectTarget }
            preserve_query_string = $false
        }
    }
    expression  = $ruleExpression
    description = $ruleDescription
    enabled     = $true
}

# Get the entrypoint ruleset for the redirect phase. 404 means "not created yet".
try {
    $entrypoint = Invoke-Cf -Method GET -Path "/zones/$zoneId/rulesets/phases/http_request_dynamic_redirect/entrypoint"
    $existingRule = $entrypoint.result.rules | Where-Object { $_.description -eq $ruleDescription }
} catch {
    if ($_.Exception.Response.StatusCode -eq 404) {
        $entrypoint = $null
        $existingRule = $null
    } else { throw }
}

if ($existingRule) {
    Step "Redirect rule already present — skip (rule id $($existingRule.id))" 'SKIP'
} else {
    if ($Apply) {
        if ($entrypoint) {
            # Update existing ruleset — append our rule.
            $rules = @($entrypoint.result.rules) + $ruleAction
            Invoke-Cf -Method PUT -Path "/zones/$zoneId/rulesets/$($entrypoint.result.id)" -Body @{ rules = $rules } | Out-Null
        } else {
            # Create entrypoint ruleset with our rule.
            $body = @{
                name  = 'default'
                kind  = 'zone'
                phase = 'http_request_dynamic_redirect'
                rules = @($ruleAction)
            }
            Invoke-Cf -Method POST -Path "/zones/$zoneId/rulesets" -Body $body | Out-Null
        }
        Step "Redirect rule → created (apex + www → $RedirectTarget, 301)" 'APPLIED'
    } else {
        Step "Redirect rule → would CREATE: apex + www → $RedirectTarget (301)" 'WOULD'
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
    Write-Host "Done. Verify with:" -ForegroundColor Green
    Write-Host "  curl -sI https://$Zone        # expect 301 -> $RedirectTarget"
    Write-Host "  curl -sI https://www.$Zone    # expect 301 -> $RedirectTarget"
    Write-Host "  dig +short $Zone              # expect Cloudflare IPs (orange-cloud proxied)"
}
