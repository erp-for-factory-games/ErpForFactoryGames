# 0023. Hosting + deployment via homelab Docker behind Cloudflare Tunnel

- Status: Accepted
- Date: 2026-05-21
- Deciders: Chris

## Context

The Satisfactory and Captain of Industry Blazor apps both have nowhere to be
deployed. Cloudflare DNS for `erp-for-factory.games` is set up (#156 / #164)
and the GitHub Pages landing placeholder (#185) serves the apex, but
`satisfactory.erp-for-factory.games` and
`captain-of-industry.erp-for-factory.games` have no origin behind them. This
blocks #180 (CoI subdomain wiring) and the Satisfactory app's public face.

Chris has two viable hosting options:

- **Homelab** — 3 Proxmox nodes already running. An existing
  `Homelab.Stacks.Infrastructure` compose stack already runs
  `cloudflared` with a registered Cloudflare Tunnel, plus Watchtower for
  auto-update on image push. So ~90% of the deployment story exists; only
  the app-specific stack is missing.
- **Azure** — a small allowance of Azure credits from a Visual Studio
  subscription. Real money once exhausted, and Azure Container Apps for
  two always-on Blazor apps would burn through it in months.

Both apps consume the user's locally-extracted CoI catalogue JSON. Mafi's
recipe data is creative content that [ADR-0009](0009-runtime-ingestion-of-game-catalogue.md)
committed not to redistribute. How that data reaches the production
container is a constraint the deployment choice has to honour.

A third option emerged during scoping: ship a small **client-side agent**
that runs on the user's machine (where the game is installed), extracts
catalogue + savegame data locally, and pushes it to the hosted web app.
This decouples the web's hosting location from where the game runs and
turns "redistribute Mafi's data" into "the user moves their own data,"
which is a fundamentally different posture. Captured as its own
milestone — see "Follow-up" below.

## Decision

Deploy as Docker containers on Chris's homelab, fronted by the existing
Cloudflare Tunnel. **Save the Azure credits for something else** — they
have a cliff edge, the homelab is already paid for and 90% configured.

Concrete shape:

- **Container images** for `src/Web/` and `src/ApiService/` — multi-stage
  builds (`mcr.microsoft.com/dotnet/sdk` → `aspnet` runtime). The CoI app
  image is **deferred to the agent milestone** — without the agent in
  place, the only catalogue source is the dev's local extract, and the
  v0 deployment scope doesn't need to solve that.
- **GitHub Actions** builds + pushes to `ghcr.io/chrisonsimtian/...` on
  tag (e.g. `v0.5.0` → `:v0.5.0` + `:latest`). Reuses the existing
  `packages: write` token pattern from the NuGet feed work.
- **GHCR visibility = public.** None of the images we ship in v0 contain
  game-publisher IP — Satisfactory's catalogue is loaded at runtime from
  the user's `Docs.json` (ADR-0009); ApiService is engine code. The CoI
  image will join once the agent path is solid and we know the image
  itself doesn't need to embed Mafi data either.
- **Compose stack lives in a new sibling repo** —
  `Homelab.Stacks.ErpForFactoryGames` — matching the existing
  `Homelab.Stacks.*` pattern. Each game/feature stack is independently
  deployable.
- **Cloudflare Tunnel ingress rules** map the subdomains to the
  internal container ports. Configured in the Cloudflare dashboard or
  via the tunnel config file in the sister repo.
- **Aspire orchestration is dev-only.** In production each container
  starts directly via its compose service; no AppHost.

## Alternatives considered

- **Azure Container Apps via `azd up`.** First-class Aspire support, no
  home-network exposure, managed TLS, predictable URL. Rejected for the
  credit-cliff problem — VS credits would last ~2–6 months for two
  always-on Blazor apps; once gone the project either pays or dies.
  Long-term planning around a finite resource is the wrong default for
  an OSS hobby project.
- **Private GHCR with the CoI catalogue baked at build time.** Original
  shape of this ADR before the agent option surfaced. Worked, but
  permanently encoded "image is private forever" as a constraint and
  required either a manual extractor run on each homelab host or a
  release-asset workflow to feed CI. The agent path supersedes it:
  user-supplied catalogue means no redistribution problem, no
  bake-at-build mechanism to maintain, no private-visibility lock-in.
- **Public GHCR images with the catalogue volume-mounted on the host.**
  Host provides the JSON via volume mount; image stays public. Works
  for one operator, doesn't scale to "anyone runs this themselves" —
  every operator would need their own catalogue refresh routine. Agent
  generalises this to any user, not just the homelab operator.
- **Bare-metal `dotnet publish` + systemd on a Proxmox VM.** No Docker
  layer, simpler stack. Rejected because Watchtower-style auto-update
  on a tagged image is more reliable than custom systemd-unit pulling,
  and the existing homelab stacks are all containerised — match the
  pattern that already works.
- **Single image bundling both apps + a reverse proxy.** Smaller
  footprint. Rejected for breaking the isolated-apps spirit of
  [ADR-0022](0022-captain-of-industry-support.md) — each game's app
  stays its own deploy unit.

## Consequences

**Easier**

- Reuses existing Cloudflare Tunnel + Watchtower stack — zero new
  external dependencies. Cloudflare DNS already proxies the subdomains;
  the tunnel terminates TLS and routes to internal HTTP.
- No paid hosting tier. Aligns with the [no-paid-licenses
  principle](../../../Users/ChrisSimon/.claude/projects/C--Source-Github-ERP-Satisfactory/memory/feedback_foundational_deps.md).
- Adding a future game's app is incremental: another Dockerfile,
  another compose service, another tunnel ingress rule. The pattern
  scales to N games without rearchitecting.
- Aspire stays the dev-time orchestration layer (`dotnet run --project
  src/AppHost` for local F5), and production runs the same images with
  no AppHost — clean separation.

**Harder**

- Home network reliability bounds the project's uptime promise. For a
  hobby planner that users only consult while playing, this is
  acceptable; for anything mission-critical it wouldn't be.
- Two-repo split (this repo + `Homelab.Stacks.ErpForFactoryGames`)
  means deploy changes happen in lockstep across two places. PR-able
  but coordination cost is real. Worth it to keep the homelab stacks
  pattern consistent.
- CoI app stays undeployable until the agent milestone lands a
  catalogue-source story. Acceptable: CoI is brand-new and the
  Satisfactory side has the larger audience. Could ship the CoI image
  with a "configure your agent" empty-state if there's urgency.

**Follow-up** (tracked under [#194](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/194))

- Dockerfiles for `src/Web/` and `src/ApiService/` (this repo).
- GitHub Actions release workflow building + pushing to GHCR on tag.
- `Homelab.Stacks.ErpForFactoryGames` repo init + compose stack +
  tunnel ingress config (sister repo, Chris-driven).
- Operator runbook in `docs/operations/deploy.md` documenting the
  tag-to-deploy lifecycle.

**Forward direction — the game agent**

A new milestone tracks the cross-platform agent that runs on the user's
game machine, extracts catalogue + savegame data locally, and pushes it
to the hosted web app. Once that lands:

- CoI image joins the public GHCR set, ships catalogue-less, reads
  per-user catalogues that the agent has uploaded.
- Live save-state ingestion becomes possible for hosted instances
  (the live-state milestones for Satisfactory and CoI both depend on
  data the web can't get from outside the user's machine).
- DNS wiring for `captain-of-industry.erp-for-factory.games`
  ([#180](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/180))
  unblocks at the same time.

**Explicitly out of scope**

- Multi-region / HA. Single homelab origin is fine.
- Migration to cloud (Azure or otherwise) if the project grows beyond
  hobby scope — that's a future ADR amendment, not this one.
- Public release of the catalogue JSON. Not happening (ADR-0009 stance
  remains).
- Per-PR ephemeral preview environments. Out of scope for v1; can be
  added later via a separate compose-on-demand setup.
