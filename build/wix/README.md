# WiX MSI — ERP Agent (Windows)

`erp-agent.wxs` is the [WiX v5](https://wixtoolset.org/) source for the agent's
Windows installer, `erp-agent-win-x64.msi` (#219). It replaces the portable zip
as the primary Windows distribution.

## What the MSI does (declaratively — no custom actions)

- Installs `erp-agent.exe` (+ the content files the publish drops next to it)
  to `C:\Program Files\ErpForFactoryGames\Agent\`.
- Registers and starts the `erp-agent` Windows service (LocalSystem,
  Automatic/delayed-start, restart-twice-on-failure).
- Registers the machine-wide `erp-agent://` protocol handler in
  `HKLM\Software\Classes\erp-agent`.
- Adds a proper Add/Remove Programs entry; `MajorUpgrade` supersedes older
  builds so `winget upgrade` and reinstalls are clean.

It deliberately does **not** seed `agent.json` / guess the save folder: a
deferred MSI action runs as SYSTEM and would resolve the wrong profile. Pairing
(deep-link or `erp-agent --setup`) runs as the user and writes that config — see
the agent-first onboarding (#297) and `../../INSTALL.md`.

## Build (Windows only)

WiX emits MSIs only on Windows. Two equivalent ways:

```powershell
# Via the Fallout target (publishes win-x64, then builds the MSI):
./build.cmd BuildMsi

# Or directly against an existing published output:
dotnet tool update --global wix --version 5.0.2
wix extension add -g WixToolset.Util.wixext
wix build build/wix/erp-agent.wxs -ext WixToolset.Util.wixext `
    -d Version=1.2.3 -d "PublishDir=<published-win-x64-dir>" `
    -arch x64 -o erp-agent-win-x64.msi
```

`Version` must be numeric `major.minor.build` (MSI ProductVersion); the build
derives it from `nbgv get-version -v SimpleVersion`.

CI builds the same MSI in the `win-x64` leg of `release-images.yml` and attaches
it to the GitHub Release alongside the zip.

## Known follow-ups

- **No embedded icon** — the agent exe ships no `<ApplicationIcon>`, so there's
  no custom Add/Remove Programs icon yet. Add one to the agent csproj, then set
  `ARPPRODUCTICON` here.
- **Code signing** — the MSI is unsigned, so SmartScreen warns on first run.
  Tracked separately (out of scope for #219).
- **Validation** — end-to-end install/uninstall is verified on the Windows test
  VM (#300); the build itself runs on the CI Windows runner.
