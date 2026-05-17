# 0019. TickerQ as the background-job scheduler

- Status: Accepted
- Date: 2026-05-17
- Deciders: Chris

## Context

Until v0.3 the API parsed the `.sav` exactly once at startup and cached the
result. `GET /factory/state` returned that snapshot forever — fresh state
required a manual `POST /factory/ingest`. Satisfactory autosaves every ~10
minutes, so the planner's view of the world went stale during play (#115).

Beyond auto-ingest, several upcoming features need a way to run periodic /
queued / delayed work in-process:

- Continuous LP-driven re-optimisation of saved plans when factory state
  drifts (out of scope of #115; a follow-up).
- Periodic catalogue refresh when `Docs.json` changes on disk.
- Plan-vs-reality drift detection on a schedule rather than on demand.

Doing each of these as bespoke `BackgroundService` classes works for one job
but doesn't scale to a handful: no shared retry policy, no persisted job
history, no observability, no manual "fire this once now" path. So this ADR
covers the *scheduler infrastructure* choice, not just the auto-ingest job.

## Decision

Use **[TickerQ](https://github.com/Arcenox-co/TickerQ) 10.4.x** as the
in-process job scheduler. Persist its operational store
(`TimeTickers`, `CronTickers`, `CronTickerOccurrences`) inside the existing
`PlanDbContext` so there is **one database, one migrations history**, and
the SQLite-default-with-Postgres-opt-in path from [ADR-0018](0018-persistence-stack.md)
stays intact.

### Integration shape

- Packages: `TickerQ` (core, includes the source generator) +
  `TickerQ.EntityFrameworkCore` (EF persistence provider). Both pinned to
  the same version.
- Wiring: `AddTickerQ(opts => opts.AddOperationalStore(efOpts =>
  efOpts.UseApplicationDbContext<PlanDbContext>(ConfigurationType.IgnoreModelCustomizer)))`
  in `AddErpPersistence`. `app.UseTickerQ()` in `Program.cs` after
  migrations apply.
- TickerQ's three entity configurations are applied **explicitly** in
  `PlanDbContext.OnModelCreating` rather than via TickerQ's
  `IModelCustomizer`. The customizer relies on the host DI being active
  during model build, which the existing `IDesignTimeDbContextFactory<T>`
  factories (`SqlitePlanDbContextFactory`, `PostgresPlanDbContextFactory`)
  intentionally bypass for `dotnet ef migrations add`. Direct
  `ApplyConfiguration` works identically at runtime and at design time so
  the model snapshot stays consistent.

### Auto-ingest job (#115)

- `[TickerFunction("auto-ingest-sav-watcher")]` on
  `AutoIngestJob.RunAsync`. No cron schedule on the attribute — the
  schedule is reconciled imperatively at startup.
- Schedule (`AutoIngestStartup.EnsureCronRegistrationAsync`) reads
  `FactoryState:Satisfactory:AutoIngest:Enabled`:
  - `true` → ensure a `CronTickerEntity` with expression `0 * * * * *`
    (once per minute) exists.
  - `false` (default) → delete the entry if present. Acceptance criterion
    in #115 is "no background activity" when disabled; attribute-level
    cron registration would always tick.
- The job polls the configured SaveGames directory, compares
  `LastWriteTimeUtc` against the currently-loaded source, and dispatches
  `IngestSaveCommand` via Wolverine when a newer save is present.

## Alternatives considered

- **`BackgroundService` from `Microsoft.Extensions.Hosting`.** Zero deps,
  ~30 lines for the auto-ingest job alone. Rejected: the second and third
  scheduled job we add (catalogue refresh, plan re-optimisation) would each
  duplicate the polling loop / retry boilerplate. Standardising on TickerQ
  now is cheaper than refactoring later.
- **[Hangfire](https://www.hangfire.io/).** Mature, ships a dashboard out
  of the box. Rejected for now: heavier than we need, the dashboard is a
  meaningful UI surface to maintain, and Hangfire's design is closer to a
  job queue than a periodic scheduler. We can revisit if we ever need
  durable user-triggered jobs (e.g. long export pipelines).
- **[Quartz.NET](https://www.quartz-scheduler.net/).** Most flexible cron
  expressions of the three. Rejected: configuration is verbose, no
  built-in dashboard, and TickerQ covers our use case with less ceremony.
- **TickerQ's own `TickerQDbContext`** (separate context, separate
  migrations). Rejected in favour of `UseApplicationDbContext<PlanDbContext>`
  so there is a single migrations history and a single SQLite file by
  default.

## Consequences

- TickerQ entities + tables added to the SQLite/Postgres schema. Migration
  is `AddTickerQOperationalStore` in both providers' folders. The
  schema lives under `ticker` (`Constants.DefaultSchema`); SQLite ignores
  schemas, Postgres uses `ticker.CronTickers` etc.
- `Program.cs` gains an `app.UseTickerQ()` call. Without it, scheduled
  rows sit dormant in the DB and nothing fires — a useful failsafe if we
  ever want to disable the scheduler in a specific environment without
  removing the migration.
- New scheduled work follows the same pattern: a class with a
  `[TickerFunction("name")]` method + either an attribute-level cron or an
  imperative reconcile at startup. Dashboard / Redis multi-node are
  available as opt-ins on the same library if the project ever grows that
  way.
- The auto-ingest job opens a service scope per tick to resolve scoped
  services (Wolverine bus is scoped). Cost is negligible at 1 tick/min.
- Continuous optimisation (#116-adjacent follow-up) and any future
  catalogue/save watchers will reuse this scheduler rather than each
  bringing their own.
