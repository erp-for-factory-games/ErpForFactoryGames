# Migration: ADR-0026 layered `src/` reshape

Working notes + checklist for the refactor PR. Stays in tree until the PR
merges, then either deletes itself or graduates into the ADR if it carries
durable wisdom.

## Status

- ADR-0026 — Accepted (committed in this branch).
- Memory pin — saved (`feedback_layered_src_layout.md`).
- Migration — **in progress** on `refactor/0026-onion-layered-src`.

## Source → target map

### Hosting (outside the onion)

| Current path | Target path | Project name change | Notes |
|---|---|---|---|
| `src/AppHost` | `src/Hosting/Erp.Hosting.AppHost` | `AppHost` → `Erp.Hosting.AppHost` | Aspire SDK; `Projects.<X>` types regenerate from new project names. |
| `src/ServiceDefaults` | `src/Hosting/Erp.Hosting.ServiceDefaults` | `ServiceDefaults` → `Erp.Hosting.ServiceDefaults` | Namespace changes from `Microsoft.Extensions.Hosting` to `Erp.Hosting.ServiceDefaults`. Consumers add explicit `using`. |

### Domain

| Current | Target | Notes |
|---|---|---|
| `src/ERP/Domain` | `src/Domain/Erp.Domain.Common` | Namespace `ERP.Domain` → `Erp.Domain.Common`. |
| (none yet) | `src/Domain/Satisfactory.Domain` | Created lazily — only if Q4 splits surface domain shapes. |
| (none yet) | `src/Domain/CaptainOfIndustry.Domain` | Same. |

### Application

| Current | Target | Notes |
|---|---|---|
| `src/ERP/Application` | `src/Application/Erp.Application.Common` | Namespace `ERP.Application` → `Erp.Application.Common`. |
| (split from `OrToolsRecipePlanner.cs`) | `src/Application/Erp.Application.Common` | The planning use-case orchestration (the public `IPlanRecipes`-shaped surface). |

### Infrastructure

| Current | Target | Notes |
|---|---|---|
| `src/ERP/Infrastructure` | `src/Infrastructure/Erp.Infrastructure` | Cross-cutting only. Strip Satisfactory-specific files out. |
| `src/ERP/Infrastructure/Persistence` | `src/Infrastructure/Persistence/Erp.Infrastructure.Persistence` | EF Core. |
| `src/Satisfactory/Catalog` | `src/Infrastructure/Satisfactory.Infrastructure` | Merge with Save. Parser, SteamLibraryDetector, DocsJsonParser. |
| `src/Satisfactory/Save` | `src/Infrastructure/Satisfactory.Infrastructure` | Save reader, building identifiers, KnownResourceNodes. |
| `src/ERP/Infrastructure/DocsCatalogProvider.cs` | `src/Infrastructure/Satisfactory.Infrastructure` | Satisfactory-specific. |
| `src/ERP/Infrastructure/SatisfactorySaveNetFactoryStateProvider.cs` | `src/Infrastructure/Satisfactory.Infrastructure` | Satisfactory-specific. |
| `src/CaptainOfIndustry/Catalog` | `src/Infrastructure/CaptainOfIndustry.Infrastructure` | CoI parser. |
| (split from `OrToolsRecipePlanner.cs`) | `src/Infrastructure/Erp.Infrastructure` | The OR-Tools wrapping; depends on `Erp.Application.Common` interface. |

### Presentation

| Current | Target | Notes |
|---|---|---|
| `src/Web` | `src/Presentation/Web/Satisfactory.Presentation.Web` | The Satisfactory webapp (currently the only one). |
| `src/Web.Shared` | `src/Presentation/Web/Erp.Presentation.Web.Common` | Cross-game web components (AgentStatusCard, AgentLogsCard). |
| `src/CaptainOfIndustry/Web` | `src/Presentation/Web/CaptainOfIndustry.Presentation.Web` | CoI webapp. |
| `src/Agent` (game-agnostic parts) | `src/Presentation/Agent/Erp.Presentation.Agent.Common` | PairingService, AgentConfigWriter, LogTailReader, LogTailBackgroundService, HttpCatalogueUploader, ICatalogueUploader/ISaveUploader/ILogTailUploader, SetupService, PairingUrlParser, AgentOptions, AgentStatus, CatalogueOptions, CatalogueUploadStartup, ServiceRegistrar. |
| `src/Agent` (Satisfactory-specific) | `src/Presentation/Agent/Satisfactory.Presentation.Agent` | SaveFolderResolver, SaveFolderWatcher, Program.cs entry point, agent.json.example, packaging/. |
| `src/ApiService` (shared) | `src/Presentation/Api/Erp.Presentation.Api.Common` | AgentTokenAuthenticator, AgentLogsStore, AgentLogsOptions, AgentUploadOptions, AgentUploadStatus, Agent endpoints, common middleware. |
| `src/ApiService` (Satisfactory) | `src/Presentation/Api/Satisfactory.Presentation.Api` | Satisfactory planner endpoints + binary entry + Dockerfile. |
| `src/ApiService` (CoI) | `src/Presentation/Api/CaptainOfIndustry.Presentation.Api` | CoI planner endpoints + binary entry + Dockerfile. |

### Tools (outside `src/`)

| Current | Target | Notes |
|---|---|---|
| `src/Deploy/Erp.Deploy` | `tools/Erp.Deploy` | `build/Build.cs` ProjectReference path updates. |

### Tests (mirror `src/`)

| Current | Target | Notes |
|---|---|---|
| `test/ERP/Domain.Tests` | `test/Domain/Erp.Domain.Common.Tests` | |
| `test/ERP/Application.Tests` | `test/Application/Erp.Application.Common.Tests` | |
| `test/ERP/Infrastructure.Tests` | `test/Infrastructure/Erp.Infrastructure.Tests` | |
| `test/ERP/Persistence.Tests` | `test/Infrastructure/Persistence/Erp.Infrastructure.Persistence.Tests` | |
| `test/Satisfactory/Catalog.Tests` + `test/Satisfactory/Save.Tests` | `test/Infrastructure/Satisfactory.Infrastructure.Tests` | Merge. |
| `test/CaptainOfIndustry/Catalog.Tests` | `test/Infrastructure/CaptainOfIndustry.Infrastructure.Tests` | |
| `test/Agent.Tests` | Split: most → `test/Presentation/Agent/Erp.Presentation.Agent.Common.Tests`; SaveFolderResolverTests → `test/Presentation/Agent/Satisfactory.Presentation.Agent.Tests`. | |
| `test/ApiService.Tests` | Split by endpoint scope: shared → `test/Presentation/Api/Erp.Presentation.Api.Common.Tests`; Satisfactory-specific → `test/Presentation/Api/Satisfactory.Presentation.Api.Tests`. | |
| `test/Web/Web.UiTests` | `test/Presentation/Web/Satisfactory.Presentation.Web.UiTests` | |
| `test/Deploy/Erp.Deploy.Tests` | `tools/Erp.Deploy.Tests` OR `test/Erp.Deploy.Tests` — TBD; following the `tools/` convention since they're tool-tests. | |

## Execution order (inside-out)

1. **Hosting** — lowest blast radius (only AppHost + ServiceDefaults; ServiceDefaults consumers update once).
2. **Domain** — innermost; all dependents (Application, Infrastructure, Presentation) need re-references.
3. **Application** — depends on Domain only.
4. **Infrastructure** — depends on Application + Domain; this layer carries the file splits.
5. **Presentation** — biggest fan-out; per-game splits of Agent + ApiService happen here.
6. **Tools** — Deploy move to `tools/`; update `build/Build.cs`.
7. **Tests** — mirror src/.
8. **slnx, CI workflows, Dockerfiles, scripts** — final pass for any remaining path references.

## Known risks / unknowns

- **Aspire `Projects.<X>` types in AppHost.** Auto-generated from ProjectReference assembly names; will regenerate as `Projects.Erp_Hosting_AppHost` etc. AppHost.cs references like `builder.AddProject<Projects.ApiService>("apiservice")` need to update to the new project names.
- **Dockerfiles** under `src/Web/Dockerfile`, `src/ApiService/Dockerfile`, `src/CaptainOfIndustry/Web/Dockerfile` will have `COPY src/Web/...` style paths that need updating.
- **CI workflows** under `.github/workflows/` reference project paths; need scanning.
- **`Persistence` test project name** is currently `ERP.Infrastructure.Persistence.Tests`. Stays in spirit; just moves folder.
- **GitHub Packages auth** required for `dotnet restore` — need `GITHUB_TOKEN` env var.

## Splits to perform

1. **`ERP/Infrastructure/OrToolsRecipePlanner.cs`** — find the OR-Tools call-site (Infrastructure) vs the use-case orchestration (Application). Split into two classes; orchestration moves to `Erp.Application.Common`, OR-Tools wrapper stays in `Erp.Infrastructure`.
2. **`Agent` project** — game-agnostic plumbing → `Erp.Presentation.Agent.Common`; Satisfactory-specific resolver + entry-point → `Satisfactory.Presentation.Agent`.
3. **`ApiService` project** — agent auth + agent endpoints + shared DI → `Erp.Presentation.Api.Common`; Satisfactory planner endpoints + entry → `Satisfactory.Presentation.Api`; CoI planner endpoints + entry → `CaptainOfIndustry.Presentation.Api`.
4. **`Satisfactory/Catalog` + `Satisfactory/Save`** — merge into one `Satisfactory.Infrastructure` project.
5. **`Agent.Tests`** — split by which side of the agent the tests cover.
6. **`ApiService.Tests`** — split by endpoint scope.

## Progress checklist

- [x] ADR-0026 written + committed.
- [x] Memory + MEMORY.md index updated.
- [x] MIGRATION-0026.md (this file).
- [ ] **Phase 1: Hosting**
  - [ ] `src/AppHost` → `src/Hosting/Erp.Hosting.AppHost`
  - [ ] `src/ServiceDefaults` → `src/Hosting/Erp.Hosting.ServiceDefaults`
  - [ ] Update consumers (`Web`, `ApiService`, `CaptainOfIndustry/Web`)
  - [ ] slnx + build green
- [ ] **Phase 2: Domain**
- [ ] **Phase 3: Application**
- [ ] **Phase 4: Infrastructure (+ splits)**
- [ ] **Phase 5: Presentation (+ splits)**
- [ ] **Phase 6: Tools**
- [ ] **Phase 7: Tests**
- [ ] **Phase 8: Final pass (slnx, CI, Dockerfiles, scripts)**
- [ ] dotnet build green
- [ ] dotnet test green
- [ ] dotnet format clean
- [ ] PR opened (draft, then ready)
