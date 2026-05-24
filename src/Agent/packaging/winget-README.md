# winget manifest

Bootstrap manifest for `ErpForFactoryGames.Agent`. Lets Windows users install
the agent with `winget install ErpForFactoryGames.Agent` and pick up new
releases via `winget upgrade`.

> This file lives outside `winget/` on purpose. `winget validate --manifest <dir>`
> parses every file in the target directory as YAML, regardless of extension,
> so a markdown file alongside the manifests trips the scanner on the first
> stray `:` it sees.

Today this is a **portable** manifest that wraps the release `erp-agent-win-x64.zip`.
Winget puts `erp-agent.exe` on the user's PATH; the user runs
`erp-agent --install` from an elevated shell to register the Windows service.
When [#219](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/219)
lands and we ship a real MSI, this manifest flips to `InstallerType: wix` and
service registration happens at install time — the user-facing
`winget install ...` command stays the same.

## Files

- `ErpForFactoryGames.Agent.installer.yaml` — installer URL, SHA256, type.
- `ErpForFactoryGames.Agent.locale.en-US.yaml` — default-locale package metadata.
- `ErpForFactoryGames.Agent.yaml` — version manifest (points at the locale).

The three files together are a single multi-file manifest per winget's schema.

## Local validation (Windows)

From a Windows machine with the winget CLI installed:

```powershell
winget validate --manifest src\Agent\packaging\winget\
winget install --manifest src\Agent\packaging\winget\
```

The second command installs the agent from these local files without
hitting winget-pkgs — useful for end-to-end testing before submitting.

## First-time submission to winget-pkgs

The first submission has to be done by hand; CI takes over from the
second release onwards.

1. Update the three yaml files so `PackageVersion`, `InstallerUrl`,
   `InstallerSha256`, and `ReleaseDate` match the release you're submitting.
2. From Windows, with [wingetcreate](https://github.com/microsoft/winget-create) installed:

   ```powershell
   wingetcreate submit --token <gh-pat-with-public_repo-scope> src\Agent\packaging\winget\
   ```

   This forks `microsoft/winget-pkgs`, places the files at
   `manifests/e/ErpForFactoryGames/Agent/<version>/`, and opens a PR.
3. Wait for the winget-pkgs review (usually hours; can be a day or two).
4. Once merged, `winget search ErpForFactoryGames` finds the package.

## Subsequent releases (CI)

The `publish-winget` job in `.github/workflows/release-images.yml` runs
after every tag push, after `publish-agent` has uploaded the zip. It calls
`wingetcreate update --submit` against this package — wingetcreate
downloads the new zip, recomputes the SHA256, forks winget-pkgs, and
opens a PR with the bumped version.

CI needs a PAT in secret `WINGET_SUBMIT_TOKEN` with `public_repo` scope.
The repo's default `GITHUB_TOKEN` isn't enough because the PR is opened
against a fork outside this repository.
