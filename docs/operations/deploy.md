# Deployment runbook

How the Blazor apps reach `*.erp-for-factory.games`. Implements
[ADR-0023](../adr/0023-hosting-deployment-approach.md).

## Topology

```
GitHub Actions (release-images.yml)
       │  on tag v*, builds + pushes
       ▼
ghcr.io/chrisonsimtian/erp-web     :v0.5.0  + :latest
ghcr.io/chrisonsimtian/erp-api     :v0.5.0  + :latest
       │
       │  Watchtower polls every 6h, pulls :latest
       ▼
Homelab Docker host (one of three Proxmox nodes)
   └─ Homelab.Stacks.ErpForFactoryGames  (sibling repo)
       ├─ erp-web         (Blazor Server, port 8080)
       └─ erp-api         (Wolverine + EF Core, port 8080)
       │
       │  internal HTTP via shared docker network
       ▼
Cloudflared tunnel (running in Homelab.Stacks.Infrastructure)
       │  ingress rules in cloudflare config
       ▼
satisfactory.erp-for-factory.games  →  http://erp-web:8080
```

The CoI app (`captain-of-industry.erp-for-factory.games`) is **not yet
deployed** — see the game-agent milestone for what's blocking it.

## Cutting a release

1. Land all the PRs you want in the release on `main`.
2. Tag the commit:

   ```powershell
   git tag v0.5.0
   git push origin v0.5.0
   ```

3. The `Release images` workflow runs (~5–10 min), builds `erp-web`
   and `erp-api`, pushes to GHCR with the tag plus `:latest`.
4. Watchtower picks up the new `:latest` on its next 6-hour cycle.
   To force-pull immediately, SSH the homelab host and run:

   ```bash
   docker compose -f /path/to/stacks/ErpForFactoryGames/compose.yml pull
   docker compose -f /path/to/stacks/ErpForFactoryGames/compose.yml up -d
   ```

## First-time setup (sister repo)

Tracked in `Homelab.Stacks.ErpForFactoryGames`. Outline:

- `compose.yml` declares two services (`erp-web`, `erp-api`),
  references the GHCR images, joins the shared `cloudflared` network.
- ApiService gets the production database connection string via env
  var or a docker secret (see ADR-0018 for the persistence picture).
- Cloudflare Tunnel ingress rules (in the tunnel config file or
  dashboard) route the subdomain to `http://erp-web:8080`.

## When things break

| Symptom | First check |
|---|---|
| 502 on the public URL | `docker logs erp-web` — common cause is ApiService not ready |
| Build fails on the GHA workflow | Bottom of the failed job — usually missing `GITHUB_TOKEN` scope or a NuGet feed flake |
| Watchtower not picking up new images | Confirm the `:latest` tag was actually pushed; check `docker logs watchtower` |
| New deploy reverts user data | ApiService DB is persisted in a named volume — verify the volume mount survived a recompose |

## Things explicitly NOT documented here

- Cloud / Azure migration. ADR-0023 chose homelab; if that changes a
  new ADR captures the move.
- CoI app deployment. Waiting on the game agent.
