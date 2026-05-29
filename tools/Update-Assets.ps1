<#
.SYNOPSIS
    Populates the .assets/ drop folder with item icons and map backdrops from
    satisfactory.wiki.gg.

.DESCRIPTION
    Per ADR-0016, the .assets/ folder is the gitignored drop zone for external
    game-derived assets (item icons, map backdrops) that the Web project serves
    at runtime via the /assets/* static-files mapping. This script is the
    reproducible way to populate it on a fresh checkout or after a game update.

    Item icons are fetched from https://satisfactory.wiki.gg/images/{Item_Name}.png
    and saved to .assets/icons/items/{Desc_ItemId_C}.png (keyed by stable in-game
    class ID, not display name). The item list comes from the running ApiService's
    /catalog/items endpoint, so the AppHost must be running first.

    Map backdrops (three files) are fetched the same way and saved to .assets/
    directly. These are the same source files copied into wwwroot/lib/maps/ per
    ADR-0015.

    The wiki is rate-limited via Cloudflare; the script paces requests at 1 req/s
    with a User-Agent that identifies the project and a one-shot 10s back-off
    on HTTP 429.

.PARAMETER CatalogUrl
    URL of the /catalog/items endpoint on the running ApiService. Default
    https://localhost:7565/catalog/items matches the Aspire launch profile.

.PARAMETER Force
    Re-download files that already exist locally. Default: skip existing files.

.EXAMPLE
    pwsh tools/Update-Assets.ps1
    # Assumes AppHost is running. Downloads only missing assets.

.EXAMPLE
    pwsh tools/Update-Assets.ps1 -Force
    # Re-downloads everything (e.g. after a game update changed icon art).
#>

[CmdletBinding()]
param(
    [string] $CatalogUrl = "https://localhost:7565/catalog/items",
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$assetsRoot = Join-Path $repoRoot '.assets'
$iconsDir   = Join-Path $assetsRoot 'icons/items'
$userAgent  = "ERP.Satisfactory-OSS-FactoryPlanner/0.1 (+https://github.com/ChrisonSimtian/ERP.Satisfactory)"
$delayMs    = 800

New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

function Get-WikiImage {
    param(
        [Parameter(Mandatory)] [string] $WikiFileName,
        [Parameter(Mandatory)] [string] $OutputPath
    )

    if ((-not $Force) -and (Test-Path $OutputPath) -and ((Get-Item $OutputPath).Length -gt 0)) {
        return 'skipped'
    }

    $url = "https://satisfactory.wiki.gg/images/$WikiFileName"
    $tmp = "$OutputPath.tmp"

    foreach ($attempt in 1..2) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $tmp -UserAgent $userAgent -MaximumRedirection 5 -ErrorAction Stop | Out-Null
            Move-Item -Force $tmp $OutputPath
            return 'ok'
        }
        catch [System.Net.WebException], [Microsoft.PowerShell.Commands.HttpResponseException] {
            $status = $_.Exception.Response.StatusCode.value__
            Remove-Item -Force -ErrorAction SilentlyContinue $tmp
            if ($status -eq 429 -and $attempt -eq 1) {
                Write-Host "  429 on $WikiFileName, backing off 10s" -ForegroundColor Yellow
                Start-Sleep -Seconds 10
                continue
            }
            return "fail($status)"
        }
    }
    return 'fail'
}

# --- 1. Map backdrops (ADR-0015) ------------------------------------------------

Write-Host "Map backdrops -> $assetsRoot" -ForegroundColor Cyan
$maps = @(
    @{ WikiName = 'Map.jpg';        LocalName = 'Map.jpg' },
    @{ WikiName = 'Biome_Map.jpg';  LocalName = 'Biome_Map.jpg' },
    @{ WikiName = 'Water_map.png';  LocalName = 'Water_map.png' }
)
foreach ($m in $maps) {
    $result = Get-WikiImage -WikiFileName $m.WikiName -OutputPath (Join-Path $assetsRoot $m.LocalName)
    Write-Host "  $($m.LocalName) -> $result"
    Start-Sleep -Milliseconds $delayMs
}

# --- 2. Item icons (ADR-0016) ---------------------------------------------------

Write-Host "Fetching catalogue from $CatalogUrl" -ForegroundColor Cyan
try {
    # -SkipCertificateCheck handles the self-signed Aspire dev certs.
    $items = Invoke-RestMethod -Uri $CatalogUrl -SkipCertificateCheck -ErrorAction Stop
}
catch {
    Write-Host ""
    Write-Host "Could not reach $CatalogUrl." -ForegroundColor Red
    Write-Host "Start the AppHost first: dotnet run --project src/Hosting/Erp.Hosting.AppHost" -ForegroundColor Red
    Write-Host "Then re-run this script." -ForegroundColor Red
    exit 1
}
Write-Host "  $($items.Count) items in catalogue"

# A handful of items have non-standard wiki filenames; the wiki dropped the
# trademark symbol for golf carts and prefixed FICSMAS snow differently from
# the rest of its FICSMAS items. Add to this map if you discover more drift.
$nameOverrides = @{
    'Desc_GolfCart_C'     = 'Factory_Cart'
    'Desc_GolfCartGold_C' = 'Golden_Factory_Cart'
    'Desc_Snow_C'         = 'Actual_Snow'
}

$ok = 0; $skip = 0; $fail = @()
Write-Host "Item icons -> $iconsDir" -ForegroundColor Cyan
foreach ($item in $items) {
    $wikiName = $nameOverrides[$item.id]
    if (-not $wikiName) { $wikiName = $item.name -replace ' ', '_' }
    $result = Get-WikiImage -WikiFileName "$wikiName.png" -OutputPath (Join-Path $iconsDir "$($item.id).png")

    switch ($result) {
        'ok'      { $ok++ }
        'skipped' { $skip++ }
        default   { $fail += "$($item.id) ($($item.name)) -> $result" }
    }
    Start-Sleep -Milliseconds $delayMs
}

Write-Host ""
Write-Host "--- summary ---" -ForegroundColor Cyan
Write-Host "  downloaded: $ok"
Write-Host "  skipped (already present): $skip"
Write-Host "  failed: $($fail.Count)"
if ($fail.Count -gt 0) {
    Write-Host ""
    Write-Host "Missing (the picker falls back to text-only for these):" -ForegroundColor Yellow
    $fail | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "If a missing item has a wiki page under a different filename, add an" -ForegroundColor Yellow
    Write-Host "entry to `$nameOverrides at the top of this script." -ForegroundColor Yellow
}
