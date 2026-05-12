#!/usr/bin/env pwsh
[CmdletBinding()]
Param([Parameter(Position = 0, ValueFromRemainingArguments = $true)] $BuildArguments)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"
trap { Write-Error $_ -ErrorAction Continue; exit 1 }

$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_NOLOGO = 1

$BuildProjectFile = Join-Path $PSScriptRoot 'build/_build.csproj'

function ExecSafe([scriptblock] $cmd) {
    & $cmd
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error '.NET SDK not found on PATH. Install it from https://dot.net or check global.json.'
    exit 1
}

ExecSafe { dotnet build $BuildProjectFile -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet }
ExecSafe { dotnet run --project $BuildProjectFile --no-build -- $BuildArguments }
