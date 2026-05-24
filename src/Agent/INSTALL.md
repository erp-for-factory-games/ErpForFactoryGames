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

Then create `%LocalAppData%\ErpForFactoryGames\agent.json` (the agent
creates this folder on first run; or make it yourself):

```json
{
  "Agent": {
    "ApiBaseUrl": "https://satisfactory.erp-for-factory.games",
    "AgentToken": "<any-non-empty-string-for-v1>"
  }
}
```

Open an **elevated** PowerShell and register the service:

```powershell
erp-agent --install
```

The service auto-starts at boot (delayed-auto), restarts on crash, and
uploads new saves as they appear. Logs at `%LocalAppData%\ErpForFactoryGames\agent-logs\`.

Future agent releases pick up with `winget upgrade ErpForFactoryGames.Agent`.

To remove: `erp-agent --uninstall` (elevated), then `winget uninstall ErpForFactoryGames.Agent`.

### Fallback: download the zip

If winget isn't an option (locked-down environment, older Windows):

1. Download `erp-agent-win-x64.zip` from the latest [GitHub Release](https://github.com/ChrisonSimtian/ErpForFactoryGames/releases).
2. Extract somewhere stable, e.g. `C:\Program Files\ErpForFactoryGames\`.
3. Create `agent.json` as above.
4. Right-click `erp-agent.exe` → **Run as administrator**, then from an
   elevated terminal: `.\erp-agent.exe --install`.

To remove: `.\erp-agent.exe --uninstall` (elevated).

## Linux (systemd)

1. Download `erp-agent-linux-x64.tar.gz` from the [GitHub Release](https://github.com/ChrisonSimtian/ErpForFactoryGames/releases).
2. Extract to `~/.local/bin/` (or anywhere on `$PATH`).
3. Edit `$XDG_CONFIG_HOME/ErpForFactoryGames/agent.json` (defaults to
   `~/.config/ErpForFactoryGames/agent.json`):

   ```json
   {
     "Agent": {
       "ApiBaseUrl": "https://satisfactory.erp-for-factory.games",
       "AgentToken": "<any-non-empty-string-for-v1>"
     }
   }
   ```

4. Register the user-mode systemd unit (no `sudo`):

   ```bash
   ~/.local/bin/erp-agent --install
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
  `%LocalAppData%\ErpForFactoryGames\agent-logs\` for the file log, and
  the Windows Event Viewer (Application log) for service-host errors.
- **Saves not uploading**: confirm `ApiBaseUrl` is correct and the
  server is reachable (`curl -i $ApiBaseUrl/health`). The agent logs
  every upload attempt with the response status code.
- **First-time `agent.json` doesn't exist**: the agent creates the
  containing folder on first run. Just create `agent.json` there
  yourself with the snippet above; it's reloaded automatically.
