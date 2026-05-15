# 0018. Persistence stack: EF Core with SQLite default, Postgres opt-in

- Status: Accepted
- Date: 2026-05-14
- Deciders: Chris

## Context

ERP.Satisfactory needs to persist user-created plans (and, later, other
write-side state — saved overrides, snapshots of catalogues, etc.). The project
is open-source, single-user by default, occasionally run as a hosted demo, and
must keep contribution friction low (zero install, `dotnet run` should "just
work").

Storage requirements:

- **Relational** — plans are aggregates with child collections (`Targets`,
  `Available`) that benefit from indexes, joins, and a normalised schema.
- **Local-first** — first-time contributors should not need to install or
  orchestrate an external database to compile and try the app.
- **Hosted-capable** — a future hosted instance with multiple users should be
  able to swap in a real RDBMS without rewriting the data layer.
- **OSS / no paid licence** — rules out commercial DB products.

The persistence-foundation work (issue #12 phase 1) shipped a provider-agnostic
EF Core layer (`PlanDbContext`, `IPlanRepository`, `SavedPlanConfiguration`)
deliberately stopping short of choosing a provider. Phase 2 picks one.

## Decision

Use **EF Core 10** with a **configurable dual-provider setup**:

- **SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`) — default. Zero install,
  file-based (`plans.db`), perfect for single-user and dev.
- **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`) — opt-in via
  `Persistence:Provider=postgres` for hosted / multi-user deployments. Aspire
  orchestration for a containerised Postgres is wired but commented out in
  `AppHost.cs`.

Provider selection happens in
`PersistenceServiceCollectionExtensions.AddErpPersistence(IConfiguration)`,
which reads `Persistence:Provider` and `ConnectionStrings:Plans` and registers
the matching `DbContext` subclass.

Two thin `DbContext` subclasses (`SqlitePlanDbContext`, `PostgresPlanDbContext`)
inherit from `PlanDbContext` — this is the EF Core-recommended pattern for
multi-provider migrations: each subclass owns its own migration set and model
snapshot. Migrations live in `Migrations/Sqlite/` and `Migrations/Postgres/`
respectively. At runtime the active subclass is also registered under the
base `PlanDbContext` service type so repositories (`PlanRepository`,
`IPlanRepository`) stay provider-agnostic.

## Alternatives considered

- **SQLite only** — simplest, but closes the door on multi-user hosting without
  a future data-layer rewrite.
- **Postgres only (always in a container)** — clean schema story but breaks
  the zero-install contributor experience.
- **A single context with both providers' migrations side-by-side** — EF Core
  rejects two `ModelSnapshot` classes keyed to the same context type. Forcing
  it (manual snapshot management, custom `IMigrationsAssembly`) was deemed
  more brittle than the two-subclass approach.
- **LiteDB / RavenDB / other embedded NoSQL** — relational shape fits the
  plan aggregate better, and the EF Core ecosystem (linq, migrations, tooling)
  is already familiar.

## Consequences

**Easier**

- Fresh clone runs against SQLite with no setup; the dev experience matches
  the rest of the stack (`dotnet run --project src/AppHost`).
- Provider switch is a one-line config change plus uncommenting the Aspire
  block — no code changes.
- The repository contract (`IPlanRepository`) stays free of provider details.

**Harder**

- Two migration sets to keep in sync. When the model changes, `migrations add`
  must run for both providers (see `PlanDbContextFactory.cs` for the commands).
  The model snapshots will diverge if this is forgotten — CI should eventually
  guard this.
- Two `DbContext` subclasses exist only to satisfy EF's per-type snapshot
  requirement; they carry no logic of their own. This is a known wart of the
  EF migrations design.
- Postgres-specific column types (e.g. `jsonb`) are unavailable while we keep
  the schema portable. If a future feature needs them, this ADR will need a
  follow-up that either picks Postgres-only or splits configurations per
  provider.

**Follow-up**

- The existing in-memory plan storage is NOT migrated to this layer yet —
  that's a separate task (likely the next phase of issue #12).
- ~~Add a CI check that `dotnet ef migrations has-pending-model-changes` is
  clean for both contexts.~~ Done in issue #81 — see _CI guards_ below.

## CI guards (issue #81)

Two CI jobs protect the dual-provider migration setup:

1. **Migration drift** — `.github/workflows/ci.yml :: migration-drift` runs the
   Nuke `CheckMigrations` target, which calls
   `dotnet ef migrations has-pending-model-changes` for both
   `SqlitePlanDbContext` and `PostgresPlanDbContext`. If the model changes
   without the matching `migrations add` running for either provider, the job
   fails. The Postgres design-time factory requires a connection-string env
   var even though `has-pending-model-changes` never opens the connection — a
   placeholder is good enough.
2. **Postgres runtime smoke** — `.github/workflows/ci.yml :: postgres-smoke`
   spins up `postgres:16` via the GitHub Actions `services` block and runs
   `dotnet ef database update` against it. Proves the migration generated for
   Postgres actually applies.

`dotnet-ef` is pinned in `.config/dotnet-tools.json` so CI gets a reproducible
version regardless of what's globally installed on the runner.

### Running the guards locally

PowerShell (Windows), bash equivalents are obvious:

```powershell
# 1) Drift guard — both providers. No external services needed; the Postgres
#    factory only wants a connection string, it never opens the connection.
$env:ERP_PERSISTENCE_CONNECTION = 'Host=localhost;Database=plans;Username=postgres;Password=placeholder'
./build.ps1 CheckMigrations

# 2) Postgres runtime smoke — needs a live Postgres. Easiest: Docker.
docker run --rm -d --name pg-smoke `
    -e POSTGRES_PASSWORD=postgres `
    -e POSTGRES_DB=plans `
    -p 5432:5432 postgres:16

$env:ERP_PERSISTENCE_CONNECTION = 'Host=localhost;Database=plans;Username=postgres;Password=postgres'
./build.ps1 MigrationsPostgresSmoke

docker stop pg-smoke
```

The underlying raw `dotnet ef` invocations (if you want to run them directly):

```powershell
# Drift, SQLite
dotnet ef migrations has-pending-model-changes `
    --project src/ERP/Infrastructure/Persistence `
    --startup-project src/ApiService `
    --context SqlitePlanDbContext

# Drift, Postgres
$env:ERP_PERSISTENCE_CONNECTION = 'Host=localhost;Database=plans;Username=postgres;Password=placeholder'
dotnet ef migrations has-pending-model-changes `
    --project src/ERP/Infrastructure/Persistence `
    --startup-project src/ApiService `
    --context PostgresPlanDbContext

# Apply Postgres schema to a live DB
$env:ERP_PERSISTENCE_CONNECTION = 'Host=localhost;Database=plans;Username=postgres;Password=postgres'
dotnet ef database update `
    --project src/ERP/Infrastructure/Persistence `
    --startup-project src/ApiService `
    --context PostgresPlanDbContext
```
