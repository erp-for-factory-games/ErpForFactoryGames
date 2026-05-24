# ERP Agent — install

The ERP Agent watches your Satisfactory save folder and uploads each
new save to the hosted planner web app, so your factory's live state
shows up alongside your plans.

Single-file self-contained binary — no .NET install required.

## Windows

### Recommended: winget

```powershell
winget install ErpForFactoryGames.Agent
```

Open an **elevated** PowerShell and register the service:

```powershell
erp-agent --install
```

`--install` seeds `%ProgramData%\ErpForFactoryGames\agent.json` with
your save folder pre-filled, grants your user write access to that
file, and registers the `erp-agent://` URL protocol handler under
`HKCU\Software\Classes\erp-agent` so the web UI's deep-link button can
launch this binary.

### Pair the install

Two ways to pair this agent to your planner account (ADR-0025 §8):

**Deep link (easiest).** In the web UI, open **My Agents** → **Add an
agent**, mint a token, then click **Open in agent**. Windows resolves
the `erp-agent://pair?token=...&api=...` URL to this binary, which
validates the token against `/api/me`, writes `agent.json`, and exits
0. Then restart the service:

```powershell
Restart-Service erp-agent
```

**CLI wizard.** Useful for headless / server installs:

```powershell
erp-agent --setup
```

Prompts for API URL, token (copy from the web UI), and an optional
save-folder override. Non-interactive variant for installers:

```powershell
erp-agent --setup --token eafg_... --api https://satisfactory.erp-for-factory.games
```

Either path writes the same `agent.json` — they just differ in how the
token gets in.

The service runs as LocalSystem; that's why `agent.json` lives under
`%ProgramData%` (machine-wide) rather than `%LocalAppData%` (per-user) —
LocalSystem and you both resolve to the same file this way.

The service auto-starts at boot (delayed-auto), restarts on crash, and
uploads new saves as they appear. Logs at `%ProgramData%\ErpForFactoryGames\agent-logs\`.

Future agent releases pick up with `winget upgrade ErpForFactoryGames.Agent`.

To remove: `erp-agent --uninstall` (elevated), then `winget uninstall ErpForFactoryGames.Agent`.

### Fallback: download the zip

If winget isn't an option (locked-down environment, older Windows):

1. Download `erp-agent-win-x64.zip` from the latest [GitHub Release](https://github.com/ChrisonSimtian/ErpForFactoryGames/releases).
2. Extract somewhere stable, e.g. `C:\Program Files\ErpForFactoryGames\`.
3. Right-click `erp-agent.exe` → **Run as administrator**, then from an
   elevated terminal: `.\erp-agent.exe --install`. This seeds
   `agent.json` under `%ProgramData%\ErpForFactoryGames\` just like the
   winget path.
4. Edit `%ProgramData%\ErpForFactoryGames\agent.json` to set the API URL
   + token, then `Restart-Service erp-agent`.

To remove: `.\erp-agent.exe --uninstall` (elevated).

## Linux (systemd)

1. Download `erp-agent-linux-x64.tar.gz` from the [GitHub Release](https://github.com/ChrisonSimtian/ErpForFactoryGames/releases).
2. Extract to `~/.local/bin/` (or anywhere on `$PATH`).
3. Register the user-mode systemd unit (no `sudo`):

   ```bash
   ~/.local/bin/erp-agent --install
   ```

   This also writes a `.desktop` entry under
   `$XDG_DATA_HOME/applications/erp-agent.desktop` and registers the
   `erp-agent://` URL protocol handler via `xdg-mime`, so the web UI's
   deep-link button can launch this binary on your desktop.

4. Pair the install. Two options (ADR-0025 §8):

   **Deep link.** In the web UI, open **My Agents** → **Add an
   agent**, mint a token, then click **Open in agent**. Your desktop
   resolves the `erp-agent://pair?token=...&api=...` URL to this
   binary, which validates the token, writes `agent.json`, and exits.

   **CLI wizard.** Useful for headless installs:

   ```bash
   ~/.local/bin/erp-agent --setup
   ```

   Or non-interactively:

   ```bash
   ~/.local/bin/erp-agent --setup --token eafg_... --api https://satisfactory.erp-for-factory.games
   ```

   Then restart so the new values take effect:

   ```bash
   systemctl --user restart erp-agent
   ```

5. To keep the agent running after you log out:

   ```bash
   loginctl enable-linger "$USER"
   ```

Logs land in `$XDG_STATE_HOME/ErpForFactoryGames/agent-logs/` (default
`~/.local/state/...`). `systemctl --user status erp-agent` shows the
journal-side view.

To remove: `~/.local/bin/erp-agent --uninstall`.

## macOS

Not supported by `--install` in v1 — the binary still runs cross-platform,
but launchd integration isn't wired yet. Run it manually in a terminal:

```bash
./erp-agent
```

A future release will add `launchctl` registration.

## What "auth" means today

There's no real authentication in v1. The agent sends an
`X-Agent-Token` header on every request; the server accepts any
non-empty string. Put a long random value in `agent.json` and treat it
like a password — when real auth lands (future ADR-0025) the upgrade
path will preserve any tokens you've already issued.

## Save folder

The agent auto-detects Satisfactory's default save folder:

- **Windows**: `%LocalAppData%\FactoryGame\Saved\SaveGames\`
- **Linux (Steam Proton)**: `~/.steam/steam/steamapps/compatdata/526870/pfx/drive_c/users/steamuser/AppData/Local/FactoryGame/Saved/SaveGames/`

If you've moved your saves elsewhere, set
`Agent:SaveFolderPath` in `agent.json` (or the
`ERP_AGENT_SaveFolderPath` env var) to the directory containing your
`.sav` files.

## Log shipping (optional, on by default)

The agent ships its local log file to the hosted planner once a minute
so you can read recent agent activity in the Web UI at `/agent/logs`.
Lines are buffered in memory on the server only (cleared on restart) —
see [ADR-0024 §9](https://github.com/ChrisonSimtian/ErpForFactoryGames/blob/main/docs/adr/0024-agent-v1-shape.md).

To disable or tune in `agent.json`:

```json
{
  "Agent": {
    "LogTail": {
      "Enabled": true,
      "Interval": "00:01:00",
      "MaxLinesPerUpload": 500
    }
  }
}
```

`Interval` is `HH:MM:SS`; `00:00:30` (30 s) is the minimum the agent
honours. Set `Enabled: false` to opt out entirely.

## Troubleshooting

- **Service installed but won't start**: check
  `%ProgramData%\ErpForFactoryGames\agent-logs\` for the file log, and
  the Windows Event Viewer (Application log) for service-host errors.
- **Saves not uploading**: confirm `ApiBaseUrl` is correct and the
  server is reachable (`curl -i $ApiBaseUrl/health`). The agent logs
  every upload attempt with the response status code.
- **`agent.json` missing on Windows**: `--install` seeds it. If you
  deleted it, re-run `erp-agent --install` (elevated) — it's idempotent
  and won't replace an existing file.
