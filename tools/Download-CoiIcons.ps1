<#
.SYNOPSIS
    Populates .assets/coi/ with product icons from wiki.coigame.com.

.DESCRIPTION
    Per ADR-0016, .assets/ is the gitignored drop zone for external game-derived
    assets that the Web project serves at runtime via the /assets/* static-files
    mapping. This script is the reproducible way to populate the Captain of
    Industry slice on a fresh checkout or after a game patch.

    Source: wiki.coigame.com (Mafi's official wiki, MediaWiki). Item icons are
    fetched via Special:FilePath/<Name>.png — a stable MediaWiki redirect that
    resolves to the actual storage hash, so we don't need to scrape pages.

    The input catalogue JSON comes from tools/CaptainOfIndustryExtractor — run
    that once first, then point this script at the resulting file (default:
    %LocalAppData%/ErpForFactoryGames/coi-catalogue.json).

    Naming: wiki uses Title_Case_With_Underscores (e.g. 'Animal_Feed.png'),
    but our catalogue stores names as 'Animal feed'. The mapper title-cases
    each word and joins with underscores. Items that don't have a wiki page
    (e.g. Bauxite_Powder, Aluminum_Scrap_Pressed) silently 404 and the
    Catalogue page falls back to text-only for those.

    Rate-limited at 1 req/s with a User-Agent identifying the project. One-shot
    10s back-off on HTTP 429.

.PARAMETER CataloguePath
    Path to the extractor's JSON output. Defaults to the same per-user app-data
    location the runtime app reads from.

.PARAMETER Force
    Re-download files that already exist locally. Default: skip existing files.

.EXAMPLE
    pwsh tools/Download-CoiIcons.ps1
    # Reads %LocalAppData%/ErpForFactoryGames/coi-catalogue.json. Skips existing files.

.EXAMPLE
    pwsh tools/Download-CoiIcons.ps1 -Force
    # Re-downloads everything (e.g. after a CoI patch changed icon art).
#>

[CmdletBinding()]
param(
    [string] $CataloguePath = (Join-Path $env:LOCALAPPDATA 'ErpForFactoryGames/coi-catalogue.json'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Resolve-Path (Join-Path $PSScriptRoot '..')
$iconsDir  = Join-Path $repoRoot '.assets/coi/icons/items'
$userAgent = 'ErpForFactoryGames-OSS-FactoryPlanner/0.1 (+https://github.com/ChrisonSimtian/ErpForFactoryGames)'
$delayMs   = 1000

if (-not (Test-Path $CataloguePath)) {
    Write-Host "Catalogue JSON not found at $CataloguePath." -ForegroundColor Red
    Write-Host "Run the extractor first:" -ForegroundColor Red
    Write-Host "  dotnet run --project tools/CaptainOfIndustryExtractor -- --install <CoI install dir>" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $iconsDir | Out-Null

function Get-WikiImage {
    param(
        [Parameter(Mandatory)] [string] $WikiFileName,
        [Parameter(Mandatory)] [string] $OutputPath
    )

    if ((-not $Force) -and (Test-Path $OutputPath) -and ((Get-Item $OutputPath).Length -gt 0)) {
        return 'skipped'
    }

    # Special:FilePath is the canonical MediaWiki redirect — resolves the
    # storage-hash path for us and is stable across image re-uploads.
    $url = "https://wiki.coigame.com/Special:FilePath/$WikiFileName"
    $tmp = "$OutputPath.tmp"

    foreach ($attempt in 1..2) {
        try {
            # -AllowInsecureRedirect: Special:FilePath bounces through http://
            # mid-chain before settling on the https://images/... target.
            # PowerShell 7 blocks downgrades by default since 7.4; we opt in
            # because the final response is https and the wiki has no https-only
            # storage host alternative.
            Invoke-WebRequest -Uri $url -OutFile $tmp -UserAgent $userAgent -MaximumRedirection 5 -AllowInsecureRedirect -ErrorAction Stop | Out-Null
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

function ConvertTo-WikiName {
    param([Parameter(Mandatory)] [string] $Name)
    # Title-case each whitespace-separated word and join with underscores.
    # Matches the wiki's filename convention (Animal_Feed.png, Carbon_Dioxide.png).
    ($Name -split '\s+' | Where-Object { $_ } | ForEach-Object {
        $_.Substring(0, 1).ToUpper() + $_.Substring(1)
    }) -join '_'
}

Write-Host "Loading catalogue from $CataloguePath" -ForegroundColor Cyan
$catalogue = Get-Content -Raw -LiteralPath $CataloguePath | ConvertFrom-Json
Write-Host "  $($catalogue.items.Count) items, extractor v$($catalogue.extractorVersion), CoI v$($catalogue.coiVersion)"

$ok = 0; $skip = 0; $fail = @()
Write-Host "Item icons -> $iconsDir" -ForegroundColor Cyan
foreach ($item in $catalogue.items) {
    $wikiName = ConvertTo-WikiName -Name $item.name
    $result   = Get-WikiImage -WikiFileName "$wikiName.png" -OutputPath (Join-Path $iconsDir "$($item.id).png")

    switch ($result) {
        'ok'      { $ok++ }
        'skipped' { $skip++ }
        default   { $fail += "$($item.id) ($($item.name)) -> $result [tried $wikiName.png]" }
    }
    Start-Sleep -Milliseconds $delayMs
}

Write-Host ""
Write-Host "--- summary ---" -ForegroundColor Cyan
Write-Host "  downloaded: $ok"
Write-Host "  skipped (already present): $skip"
Write-Host "  missing: $($fail.Count)"
if ($fail.Count -gt 0) {
    Write-Host ""
    Write-Host "Items without a wiki icon (Catalogue page falls back to text-only):" -ForegroundColor Yellow
    $fail | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}
