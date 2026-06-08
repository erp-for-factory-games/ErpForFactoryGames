using Erp.Presentation.Api.Common;
using Satisfactory.Presentation.Api;
using Erp.Hosting.ServiceDefaults;
using Erp.Application.Common;
using Erp.Application.Common.Commands.IngestSave;
using Erp.Application.Common.Queries.PlanProduction;
using Erp.Domain.Common;
using Erp.Infrastructure;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Satisfactory.Infrastructure;
using TickerQ.DependencyInjection;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ICatalogProvider).Assembly);
});

builder.Services.AddErpInfrastructure(builder.Configuration);

// ---- Plan persistence (EF Core, ADR-0018) ----------------------------------
// SQLite by default, Postgres opt-in via `Persistence:Provider=postgres`.
// Connection string lives in `ConnectionStrings:Plans`.
builder.Services.AddErpPersistence(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Agent upload tracking + config. Server-side counterpart to the agent's
// IAgentStatus. Singleton — atomic snapshot replacement per upload.
builder.Services.Configure<AgentUploadOptions>(builder.Configuration.GetSection("AgentUploads"));
builder.Services.AddSingleton<IAgentUploadStatus, AgentUploadStatus>();

// Agent log-tail ring buffer (#210). In-memory only — see ADR-0024 §9.
// Durable cross-process observability is the follow-up issue #212 (SigNoz / OTel).
builder.Services.Configure<AgentLogsOptions>(builder.Configuration.GetSection("AgentLogs"));
builder.Services.AddSingleton<IAgentLogsStore, AgentLogsStore>();

// Agent auth pipeline (ADR-0027 / 5c3). Hybrid authenticator: a JWT minted by
// the Auth API is verified locally via the shared HMAC key (no DB hit), with a
// fallback to the legacy eafg_* hash-DB lookup during the deprecation window.
// The hashing algorithm + token repo are wired by AddErpInfrastructure.
builder.Services.AddAgentTokenAuth(builder.Configuration);
// DevPlayerBootstrap stays on the Auth API (phase 5c2) — the Player + AgentToken
// aggregate is owned there.

// Current-player accessor (ADR-0025 §2 / ADR-0028 §3). Auth:Backend selects the
// adapter: dev -> Auth:DevPlayerId; keycloak -> the validated OIDC sub from the
// forwarded Keycloak access token (and registers the JWT-bearer scheme). The
// rest of the graph (PlayerScopedCatalogProvider) picks up ICurrentPlayer
// unchanged. The agent X-Agent-Token path is independent and untouched.
builder.Services.AddErpUserAuth(builder.Configuration);

// Catalogue storage (ADR-0025 §4-§5). Bytes land on the filesystem; the
// PlayerCatalogue EF row carries the metadata + dedup hash.
builder.Services.Configure<CatalogueStorageOptions>(
    builder.Configuration.GetSection(CatalogueStorageOptions.SectionName));
builder.Services.AddSingleton<ICatalogueStorage, FileSystemCatalogueStorage>();

var app = builder.Build();

// Apply pending plan-storage migrations on startup so the SQLite default Just
// Works on a fresh checkout and so saved plans survive process restarts (#77).
// Unconditional: the only currently-supported runtime layout is the embedded
// SQLite file alongside the app. If/when a hosted Postgres deploy lands, gate
// this on environment (or move to a Wolverine startup migration).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
    db.Database.Migrate();
}

// Auto-ingest cron registration (#115). Idempotent: ensures a TickerQ
// cron entry for the AutoIngestJob exists if AutoIngest:Enabled=true,
// removes it otherwise. Runs after migrations so the TickerQ tables exist.
await AutoIngestStartup.EnsureCronRegistrationAsync(app.Services);

// Activate the TickerQ job processor. Without this call the cron entries
// sit dormant in the DB and nothing fires.
app.UseTickerQ();

app.UseExceptionHandler();

// User-facing auth (ADR-0028 #292). No-op when Auth:Backend=dev; under keycloak
// it populates HttpContext.User from the forwarded Keycloak access token so
// PlayerScopedCatalogProvider scopes the catalogue to the signed-in player.
// The agent /api/agent/* endpoints validate X-Agent-Token themselves and are
// unaffected (they carry no [Authorize] metadata, so nothing is gated here).
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running. See /catalog/items, /plan, /factory/state.");

app.MapGet("/catalog/items", (ICatalogProvider catalog, IOptions<CatalogueOptions> catOpts) =>
    NoCatalogueProblem.IfMissing(catalog, catOpts) ?? Results.Ok(catalog.Items
        .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        .Select(i => new ItemDto(i.Id.Value, i.Name))));

app.MapGet("/catalog/recipes", (ICatalogProvider catalog, IOptions<CatalogueOptions> catOpts) =>
{
    if (NoCatalogueProblem.IfMissing(catalog, catOpts) is { } missing) return missing;
    // Per-minute amounts mirror what /plan returns and what the planner UI displays —
    // raw per-cycle counts on the wire would force every consumer to multiply by
    // 60/duration. Recipes with zero duration would be a parser bug, but guard anyway.
    AmountDto ToPerMinute(ItemAmount a, TimeSpan duration) =>
        new(a.Item.Value,
            catalog.FindItem(a.Item)?.Name ?? a.Item.Value,
            duration.TotalSeconds > 0
                ? Math.Round(a.Quantity * 60m / (decimal)duration.TotalSeconds, 4)
                : a.Quantity);

    return Results.Ok(catalog.Recipes
        .OrderBy(r => r.IsAlternate)
        .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .Select(r =>
        {
            var building = catalog.FindBuilding(r.Building);
            return new RecipeView(
                r.Id.Value,
                r.Name,
                r.Building.Value,
                building?.Name ?? r.Building.Value,
                building?.BasePowerMw ?? 0,
                r.IsAlternate,
                r.Duration.TotalSeconds,
                r.Inputs.Select(i => ToPerMinute(i, r.Duration)).ToList(),
                r.Outputs.Select(o => ToPerMinute(o, r.Duration)).ToList());
        }));
});

app.MapGet("/catalogue/status", (ICatalogProvider catalog) => catalog.GetStatus());

app.MapPost("/catalogue/configure", (ConfigureCatalogueRequest request, ICatalogProvider catalog) =>
{
    if (string.IsNullOrWhiteSpace(request.DocsPath))
        return Results.BadRequest(new { error = "DocsPath is required." });

    try
    {
        var status = catalog.LoadFromPath(request.DocsPath);
        return Results.Ok(status);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to load catalogue", detail: ex.Message, statusCode: 422);
    }
});

app.MapGet("/factory/state", (IFactoryStateProvider provider, ICatalogProvider catalog, IOptions<CatalogueOptions> catOpts) =>
    NoCatalogueProblem.IfMissing(catalog, catOpts) ?? Results.Ok(FactoryStateView.From(provider, catalog)));

app.MapGet("/factory/state.geojson", (IFactoryStateProvider provider, ICatalogProvider catalog, Satisfactory.Infrastructure.KnownFlora flora, IOptions<CatalogueOptions> catOpts) =>
    NoCatalogueProblem.IfMissing(catalog, catOpts) ?? Results.Json(FactoryStateGeoJson.From(provider, catalog, flora), contentType: "application/geo+json"));

app.MapGet("/factory/saves", () =>
    SaveFileResolver.EnumerateDetectedSaves()
        .Select(f => new DetectedSaveView(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
        .ToList());

// Active factory bottleneck alerts (#116). Read by the ADA agent so she leads
// with active alerts on each user turn. Empty list when nothing's flagged.
// Writes happen post-ingest via the analysis service.
app.MapGet("/factory/alerts", async (IFactoryAlertRepository repo, CancellationToken ct) =>
{
    var alerts = await repo.ListActiveAsync(ct);
    return Results.Ok(alerts.Select(FactoryAlertView.From).ToList());
});

// Manual dismissal (#116, phase C). Marks an alert as dismissed; subsequent
// analysis passes that re-detect the same condition will create a *new* alert
// rather than re-fire the dismissed one. Idempotent — re-dismissing an
// already-dismissed alert is a 204, not an error.
app.MapPost("/factory/alerts/{id:guid}/dismiss", async (
    Guid id, IFactoryAlertRepository repo, TimeProvider clock, CancellationToken ct) =>
{
    var alert = await repo.GetAsync(id, ct);
    if (alert is null) return Results.NotFound();

    alert.Dismiss(clock.GetUtcNow().UtcDateTime);
    await repo.SaveChangesAsync(ct);
    return Results.NoContent();
});

// Backing endpoint for the in-app filesystem picker (issue #84). Lists the
// directory at `path` so the Blazor `PathPickerDialog` can render breadcrumbs +
// folder/file rows. Read-only enumeration of an inherently-local dev tool —
// no auth gate, but every IO call is wrapped so a denied / missing path
// becomes a structured error instead of a 500.
//
// `purpose` is an optional hint ("catalogue" | "saves") used to compute a
// smart starting directory when the caller hasn't given an explicit `path` —
// the picker can land the user inside Satisfactory's Docs/SaveGames folder
// instead of making them click through ~/Library/... by hand.
app.MapGet("/fs/browse", (string? path, string? filter, string? purpose) =>
{
    var startPath = ResolveStartPath(path, purpose);

    DirectoryInfo dir;
    try
    {
        dir = new DirectoryInfo(startPath);
        if (!dir.Exists)
        {
            // Silently fall back so a stale user-stored path doesn't dead-end the picker.
            dir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Invalid path", detail: ex.Message, statusCode: 400);
    }

    var allowed = ParseFilter(filter);
    var dirs = new List<FsEntryView>();
    var files = new List<FsEntryView>();

    IEnumerable<DirectoryInfo> subDirs;
    IEnumerable<FileInfo> entries;
    try
    {
        subDirs = dir.EnumerateDirectories();
        entries = dir.EnumerateFiles();
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(title: "Access denied", detail: ex.Message, statusCode: 403);
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to enumerate", detail: ex.Message, statusCode: 422);
    }

    foreach (var sub in subDirs.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
    {
        // Skip hidden entries on Unix-likes — Library, .git, etc. clutter the picker.
        if (sub.Name.StartsWith('.')) continue;
        try
        {
            dirs.Add(new FsEntryView(sub.Name, sub.FullName, true, sub.LastWriteTimeUtc, null));
        }
        catch
        {
            // Symlink target missing, permission etc. — skip silently.
        }
    }

    foreach (var f in entries.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
    {
        if (f.Name.StartsWith('.')) continue;
        if (allowed.Count > 0 && !allowed.Contains(f.Extension.ToLowerInvariant())) continue;
        files.Add(new FsEntryView(f.Name, f.FullName, false, f.LastWriteTimeUtc, f.Length));
    }

    return Results.Ok(new FsBrowseView(dir.FullName, dir.Parent?.FullName, dirs, files));

    static string ResolveStartPath(string? path, string? purpose)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(path))
        {
            var expanded = ExpandHome(path);
            // If the caller hands us a file (e.g. a previously-picked Docs.json
            // or .sav), land in its directory so the picker shows siblings.
            if (File.Exists(expanded))
                return Path.GetDirectoryName(expanded) ?? home;
            return expanded;
        }

        foreach (var candidate in CandidatesFor(purpose, home))
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        return home;
    }

    // Probe order matters: Satisfactory Docs/SaveGames first so the user lands
    // exactly where they need to pick, with the user's profile dir as the
    // universal last-resort. The macOS bottle name varies (users can name
    // bottles anything), so we enumerate `~/Library/Application Support/
    // CrossOver/Bottles/*` rather than hard-coding "Steam".
    static IEnumerable<string> CandidatesFor(string? purpose, string home)
    {
        if (string.IsNullOrWhiteSpace(purpose)) yield break;

        // For each base install root we emit tiered candidates: the exact target
        // first (Docs/), then a less-specific fallback (the install root) so a
        // SF 1.0+ user — whose catalogue lives inside .pak, not in CommunityResources —
        // still lands at the install instead of `~/`.
        if (OperatingSystem.IsMacOS())
        {
            var bottlesRoot = Path.Combine(home, "Library/Application Support/CrossOver/Bottles");
            if (Directory.Exists(bottlesRoot))
            {
                foreach (var bottle in Directory.EnumerateDirectories(bottlesRoot))
                {
                    var install = Path.Combine(bottle, "drive_c/Program Files (x86)/Steam/steamapps/common/Satisfactory");
                    if (purpose == "catalogue")
                    {
                        yield return Path.Combine(install, "CommunityResources/Docs");
                        yield return Path.Combine(install, "CommunityResources");
                        yield return install;
                    }
                    else if (purpose == "saves")
                    {
                        yield return Path.Combine(bottle, "drive_c/users/crossover/AppData/Local/FactoryGame/Saved/SaveGames");
                    }
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            if (purpose == "catalogue")
            {
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs";
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources";
                yield return @"C:\Program Files (x86)\Steam\steamapps\common\Satisfactory";
                yield return @"C:\Program Files\Epic Games\SatisfactoryEarlyAccess\CommunityResources\Docs";
                yield return @"C:\Program Files\Epic Games\SatisfactoryEarlyAccess";
            }
            else if (purpose == "saves")
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                    yield return Path.Combine(localAppData, "FactoryGame", "Saved", "SaveGames");
            }
        }
    }

    static string ExpandHome(string p)
    {
        if (p.StartsWith("~/", StringComparison.Ordinal) || p == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return p == "~" ? home : Path.Combine(home, p[2..]);
        }
        return p;
    }

    static HashSet<string> ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
});

app.MapPost("/factory/ingest", async (
    IngestSaveRequest request, IMessageBus bus, IFactoryStateProvider provider,
    ICatalogProvider catalog, FactoryAlertAnalysisService alertAnalysis,
    IOptions<CatalogueOptions> catOpts,
    ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.SavePath))
        return Results.BadRequest(new { error = "SavePath is required." });

    if (NoCatalogueProblem.IfMissing(catalog, catOpts) is { } missing) return missing;

    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await bus.InvokeAsync<FactoryStateStatus>(new IngestSaveCommand(request.SavePath));
        sw.Stop();
        var ingestLogger = loggerFactory.CreateLogger("FactoryIngestEndpoint");
        ingestLogger.LogInformation("Ingested save in {Elapsed}ms", sw.ElapsedMilliseconds);

        // Run alert analysis post-ingest (#116). Best-effort: failures here
        // are logged but never fail the ingest itself — the save loaded
        // successfully, and alerts are an auxiliary surface.
        try
        {
            var source = $"save:{System.IO.Path.GetFileNameWithoutExtension(request.SavePath)}";
            await alertAnalysis.RunAsync(source, ct);
        }
        catch (Exception alertEx)
        {
            ingestLogger.LogWarning(alertEx, "Alert analysis failed post-ingest; ingest itself succeeded.");
        }

        return Results.Ok(FactoryStateView.From(provider, catalog));
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Failed to parse save", detail: ex.Message, statusCode: 422);
    }
});

// ----- Manual node overrides (#42 Option B) ---------------------------------
// User-curated resource + purity for individual BP_ResourceNode_C actors.
// Persisted to %LOCALAPPDATA%\ERP.Satisfactory\manual-node-overrides.json.
// Body identifies the node by `reference` (the in-save PathName, surfaced in
// GeoJSON as the resource-node feature's `kind`). The server resolves the
// node's position from current state, persists at that position (so the
// override survives across saves of the same world), and refreshes parsed
// state so callers see the change immediately.

app.MapPut("/factory/node-override", (
    NodeOverrideRequest request,
    Satisfactory.Infrastructure.ManualNodeOverrides overrides,
    IFactoryStateProvider provider) =>
{
    if (string.IsNullOrWhiteSpace(request.Reference))
        return Results.BadRequest(new { error = "Reference is required." });
    if (string.IsNullOrWhiteSpace(request.Resource))
        return Results.BadRequest(new { error = "Resource is required (e.g. Desc_OreIron_C)." });
    if (!Enum.TryParse<Erp.Domain.Common.NodePurity>(request.Purity, ignoreCase: true, out var purity))
        return Results.BadRequest(new { error = $"Unknown purity '{request.Purity}'. Use Impure, Normal, or Pure." });

    var node = provider.Current.ResourceNodes
        .FirstOrDefault(n => string.Equals(n.Reference, request.Reference, StringComparison.Ordinal));
    if (node is null)
        return Results.NotFound(new { error = $"No resource node with reference '{request.Reference}'." });

    overrides.Upsert(node.Position, request.Resource, purity);
    provider.Refresh();
    return Results.NoContent();
});

app.MapDelete("/factory/node-override", (
    string reference,
    Satisfactory.Infrastructure.ManualNodeOverrides overrides,
    IFactoryStateProvider provider) =>
{
    if (string.IsNullOrWhiteSpace(reference))
        return Results.BadRequest(new { error = "reference query parameter is required." });

    var node = provider.Current.ResourceNodes
        .FirstOrDefault(n => string.Equals(n.Reference, reference, StringComparison.Ordinal));
    if (node is null)
        return Results.NotFound(new { error = $"No resource node with reference '{reference}'." });

    var removed = overrides.Delete(node.Position);
    if (removed) provider.Refresh();
    return Results.NoContent();
});

app.MapPost("/plan", async (PlanRequest request, IMessageBus bus, ICatalogProvider catalog, IOptions<CatalogueOptions> catOpts, ILoggerFactory loggerFactory) =>
{
    if (NoCatalogueProblem.IfMissing(catalog, catOpts) is { } missing) return missing;

    var query = new PlanProductionQuery(
        Targets: request.Targets.Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute)).ToList(),
        Available: request.Available.Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute)).ToList(),
        Nodes: request.Nodes?.Select(n => new NodeAvailability(
            NodeReference: n.NodeReference,
            Resource: new ItemId(n.Resource),
            Purity: Enum.TryParse<NodePurity>(n.Purity, ignoreCase: true, out var p) ? p : NodePurity.Normal,
            AvailableTiers: n.AvailableTiers?
                .Select(s => Enum.TryParse<MinerTier>(s, ignoreCase: true, out var t) ? t : MinerTier.Mk1)
                .ToList())).ToList(),
        PowerTargetMw: request.PowerTargetMw);

    var logger = loggerFactory.CreateLogger("PlannerEndpoint");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var plan = await bus.InvokeAsync<ProductionPlan>(query);
    sw.Stop();
    logger.LogInformation(
        "Planner: {Targets} target(s) → {Steps} step(s), {Missing} missing input(s) in {Elapsed}ms",
        query.Targets.Count, plan.Steps.Count, plan.MissingInputs.Count, sw.ElapsedMilliseconds);

    return Results.Ok(PlanDto.From(plan, catalog));
});

// ---- Saved plans (ADR-0018, issue #77) -------------------------------------
// Persist the user's planner inputs (targets + available resources) so they
// survive a process restart. Computed plans are NOT persisted — they're a pure
// function of (catalogue, targets, available) and re-running the planner on
// load keeps results valid across catalogue updates.

app.MapGet("/plans", async (IPlanRepository repo, CancellationToken ct) =>
{
    var plans = await repo.ListAsync(ct);
    return Results.Ok(plans.Select(SavedPlanSummaryDto.From).ToList());
});

app.MapGet("/plans/{id:guid}", async (Guid id, IPlanRepository repo, CancellationToken ct) =>
{
    var plan = await repo.GetAsync(id, ct);
    return plan is null ? Results.NotFound() : Results.Ok(SavedPlanDto.From(plan));
});

app.MapPost("/plans", async (SavePlanRequest request, IPlanRepository repo, TimeProvider clock, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Name is required." });

    var nowUtc = clock.GetUtcNow().UtcDateTime;
    var plan = new SavedPlan(
        id: Guid.NewGuid(),
        name: request.Name.Trim(),
        targets: (request.Targets ?? []).Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute)).ToList(),
        available: (request.Available ?? []).Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute)).ToList(),
        createdUtc: nowUtc,
        updatedUtc: nowUtc);

    await repo.AddAsync(plan, ct);
    await repo.SaveChangesAsync(ct);
    return Results.Created($"/plans/{plan.Id}", SavedPlanDto.From(plan));
});

app.MapPut("/plans/{id:guid}", async (Guid id, SavePlanRequest request, IPlanRepository repo, TimeProvider clock, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Name is required." });

    var existing = await repo.GetAsync(id, ct);
    if (existing is null) return Results.NotFound();

    var nowUtc = clock.GetUtcNow().UtcDateTime;
    existing.Rename(request.Name.Trim(), nowUtc);
    existing.Replace(
        (request.Targets ?? []).Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute)).ToList(),
        (request.Available ?? []).Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute)).ToList(),
        nowUtc);

    await repo.UpdateAsync(existing, ct);
    await repo.SaveChangesAsync(ct);
    return Results.Ok(SavedPlanDto.From(existing));
});

app.MapDelete("/plans/{id:guid}", async (Guid id, IPlanRepository repo, CancellationToken ct) =>
{
    var removed = await repo.DeleteAsync(id, ct);
    if (!removed) return Results.NotFound();
    await repo.SaveChangesAsync(ct);
    return Results.NoContent();
});

// ----- Share links (#80) ----------------------------------------------------

app.MapPost("/plans/{id:guid}/share", async (
    Guid id,
    IPlanRepository plans,
    IPlanShareRepository shares,
    TimeProvider clock,
    HttpRequest http,
    CancellationToken ct) =>
{
    var plan = await plans.GetAsync(id, ct);
    if (plan is null) return Results.NotFound();

    var token = ShareTokenGenerator.NewToken();
    var entity = new PlanShareToken(token, plan.Id, clock.GetUtcNow().UtcDateTime);
    await shares.AddAsync(entity, ct);
    await shares.SaveChangesAsync(ct);

    var baseUrl = $"{http.Scheme}://{http.Host}";
    return Results.Ok(new ShareTokenView(token, $"{baseUrl}/plans/shared/{token}", entity.CreatedUtc, entity.ExpiresUtc));
});

app.MapDelete("/plans/{id:guid}/share/{token}", async (
    Guid id,
    string token,
    IPlanShareRepository shares,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var entity = await shares.GetAsync(token, ct);
    if (entity is null || entity.PlanId != id) return Results.NotFound();

    entity.Revoke(clock.GetUtcNow().UtcDateTime);
    await shares.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.MapGet("/plans/shared/{token}", async (
    string token,
    IPlanRepository plans,
    IPlanShareRepository shares,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var entity = await shares.GetAsync(token, ct);
    if (entity is null || !entity.IsActive(clock.GetUtcNow().UtcDateTime))
        return Results.NotFound();

    var plan = await plans.GetAsync(entity.PlanId, ct);
    return plan is null ? Results.NotFound() : Results.Ok(SavedPlanDto.From(plan));
});

// ---------------------------------------------------------------------------
// Agent endpoints (#199). Wire shape per ADR-0024 §4 + §5.
//
//   POST /api/agent/savegames/satisfactory  — raw .sav body, three X-Agent-*
//     headers. Token validated via IAgentTokenAuthenticator (ADR-0025).
//     Parser failures land 422 with the exception type + first-line of the
//     message in the body.
//
//   GET  /api/agent/status                  — last upload snapshot for the
//     Web UI status card (#200).
//
// All public agent-facing endpoints live under /api/ so the Cloudflare
// tunnel can route /api/.* to erp-api separately from the Blazor pages on
// erp-web — see #242. The Blazor page at @page "/agent/logs" stays on
// erp-web; the API endpoint moved to /api/agent/logs to avoid the collision.
// ---------------------------------------------------------------------------

app.MapPost("/api/agent/savegames/satisfactory", async (
    HttpRequest http,
    IFactoryStateProvider provider,
    IAgentUploadStatus uploadStatus,
    IAgentTokenAuthenticator authenticator,
    Microsoft.Extensions.Options.IOptions<AgentUploadOptions> uploadOptions,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var log = loggerFactory.CreateLogger("AgentUpload");

    var auth = await authenticator.AuthenticateAsync(http.Headers["X-Agent-Token"].ToString(), ct).ConfigureAwait(false);
    if (!auth.IsAuthenticated)
    {
        return Results.Json(new { error = "X-Agent-Token is missing or invalid." }, statusCode: 401);
    }

    if (!string.Equals(http.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(
            new { error = $"Content-Type must be application/octet-stream; got '{http.ContentType}'." },
            statusCode: 415);
    }

    var opts = uploadOptions.Value;
    if (http.ContentLength is { } len && len > opts.MaxUploadBytes)
    {
        return Results.Json(
            new { error = $"Upload exceeds MaxUploadBytes ({opts.MaxUploadBytes})." },
            statusCode: 413);
    }

    var fileNameHeader = http.Headers["X-Agent-FileName"].ToString();
    var displayName = string.IsNullOrWhiteSpace(fileNameHeader)
        ? "uploaded.sav"
        : Uri.UnescapeDataString(fileNameHeader);
    var agentVersion = http.Headers["X-Agent-Version"].ToString();
    if (string.IsNullOrWhiteSpace(agentVersion)) agentVersion = null;

    var uploadDir = opts.ResolveUploadDirectory();
    Directory.CreateDirectory(uploadDir);
    var targetPath = Path.Combine(uploadDir, "satisfactory-latest.sav");
    var tempPath = targetPath + ".upload";

    try
    {
        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: 64 * 1024, useAsync: true))
        {
            await http.Body.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        File.Move(tempPath, targetPath, overwrite: true);

        var parsedAt = DateTimeOffset.UtcNow;
        try
        {
            var status = provider.LoadFromPath(targetPath);
            uploadStatus.Record(new UploadSnapshot(
                FileName: displayName,
                ParsedAt: parsedAt,
                SaveVersion: status.SaveVersion,
                BuildVersion: status.BuildVersion,
                Succeeded: true,
                StatusCode: 200,
                Detail: null,
                AgentVersion: agentVersion));
            log.LogInformation(
                "Agent upload {File} ({Bytes} bytes, agent v{Agent}) → save v{SaveVersion}, build {BuildVersion}",
                displayName, http.ContentLength ?? -1, agentVersion ?? "?", status.SaveVersion, status.BuildVersion);
            return Results.Ok(new
            {
                saveVersion = status.SaveVersion,
                buildVersion = status.BuildVersion,
                parsedAt,
            });
        }
        catch (Exception parseEx)
        {
            uploadStatus.Record(new UploadSnapshot(
                FileName: displayName,
                ParsedAt: parsedAt,
                SaveVersion: null,
                BuildVersion: null,
                Succeeded: false,
                StatusCode: 422,
                Detail: parseEx.GetType().Name + ": " + parseEx.Message,
                AgentVersion: agentVersion));
            log.LogWarning(parseEx, "Agent upload {File} parse failed", displayName);
            return Results.Json(
                new { error = "Save parse failed.", detail = parseEx.Message, type = parseEx.GetType().Name },
                statusCode: 422);
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Agent upload {File} threw before/after parse", displayName);
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
        return Results.Problem(title: "Upload failed", detail: ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/agent/status", (IAgentUploadStatus status, TimeProvider clock) =>
{
    var latest = status.Latest;
    // isStale = no upload in the last 10 minutes. The watcher only fires
    // on save change; expect long quiet periods on a paused game. Tune
    // later if the UI complains.
    var stalenessThreshold = TimeSpan.FromMinutes(10);
    var now = clock.GetUtcNow();
    var isStale = latest is null || (now - latest.ParsedAt) > stalenessThreshold;

    return Results.Ok(new
    {
        lastUpload = latest,
        agentSeen = latest?.ParsedAt,
        isStale,
    });
});

// ---- Agent log-tail (#210) ------------------------------------------------
//
//   POST /api/agent/logs — JSON body `{ lines: ["..."], agentVersion?: "..." }`.
//     Same X-Agent-Token seam as the save upload (ADR-0024 §5).
//
//   GET  /api/agent/logs?limit=N — most-recent lines from the ring buffer.

app.MapPost("/api/agent/logs", async (
    HttpRequest http,
    IAgentLogsStore store,
    IAgentTokenAuthenticator authenticator,
    IPlayerRepository players,
    Microsoft.Extensions.Options.IOptions<AgentLogsOptions> logsOptions,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var auth = await authenticator.AuthenticateAsync(http.Headers["X-Agent-Token"].ToString(), ct).ConfigureAwait(false);
    if (!auth.IsAuthenticated)
    {
        return Results.Json(new { error = "X-Agent-Token is missing or invalid." }, statusCode: 401);
    }

    if (!string.Equals(http.ContentType, "application/json", StringComparison.OrdinalIgnoreCase)
        && http.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
    {
        return Results.Json(
            new { error = $"Content-Type must be application/json; got '{http.ContentType}'." },
            statusCode: 415);
    }

    AgentLogsRequest? payload;
    try
    {
        payload = await http.ReadFromJsonAsync<AgentLogsRequest>(ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Invalid JSON body.", detail = ex.Message }, statusCode: 400);
    }

    // ReIngestRequested piggybacks on this poll (ADR-0025 §7) — the agent
    // reads it on every log-tail tick and triggers CatalogueUploader on
    // true. Looked up once per request; cheap with the PlayerId already
    // resolved by the auth pipeline.
    var player = await players.GetAsync(auth.PlayerId, ct).ConfigureAwait(false);
    var reIngestRequested = player?.ReIngestRequested ?? false;

    if (payload is null || payload.Lines is null || payload.Lines.Count == 0)
    {
        return Results.Ok(new { received = 0, reIngestRequested });
    }

    var cap = Math.Max(1, logsOptions.Value.MaxLinesPerRequest);
    var lines = payload.Lines.Count > cap
        ? payload.Lines.Take(cap).ToArray()
        : (IEnumerable<string>)payload.Lines;

    var agentVersion = http.Headers["X-Agent-Version"].ToString();
    if (string.IsNullOrWhiteSpace(agentVersion)) agentVersion = payload.AgentVersion;
    if (string.IsNullOrWhiteSpace(agentVersion)) agentVersion = null;

    store.Append(lines, agentVersion, clock.GetUtcNow());

    return Results.Ok(new { received = payload.Lines.Count, retained = store.TotalReceived, reIngestRequested });
});

app.MapGet("/api/agent/logs", (IAgentLogsStore store, int? limit) =>
{
    var take = limit is > 0 ? Math.Min(limit.Value, 5000) : 500;
    var lines = store.ReadLatest(take);
    return Results.Ok(new
    {
        lines = lines.Select(l => new
        {
            text = l.Text,
            uploadedAt = l.UploadedAt,
            agentVersion = l.AgentVersion,
        }),
        totalReceived = store.TotalReceived,
        agentLastSeen = store.AgentLastSeen,
    });
});

// ---- Catalogue upload (ADR-0025 §4-§5) ------------------------------------
//
//   POST /api/agent/catalogue/satisfactory — raw Docs.json body.
//     Headers:
//       X-Agent-Token       — validated by IAgentTokenAuthenticator.
//       If-None-Match       — last-uploaded hash echoed by the agent so
//                             identical re-uploads short-circuit to 304.
//     200 { docsHash, sizeBytes, uploadedUtc, changed: true }
//     304 (changed=false; If-None-Match matched the stored hash)
//     401 missing/invalid token
//     413 body exceeds MaxUploadBytes
//     415 unexpected Content-Type (must be application/json or octet-stream)
//
// Bytes are kept opaque in v2 — no Docs.json parsing at upload time.
// GameVersion parsing lands with the planner resolver swap (Phase B
// follow-up to #238).
// ---------------------------------------------------------------------------

app.MapPost("/api/agent/catalogue/satisfactory", async (
    HttpRequest http,
    IAgentTokenAuthenticator authenticator,
    IPlayerCatalogueRepository catalogues,
    IPlayerRepository players,
    ICatalogueStorage storage,
    Microsoft.Extensions.Options.IOptions<CatalogueStorageOptions> storageOptions,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var log = loggerFactory.CreateLogger("CatalogueUpload");

    var auth = await authenticator.AuthenticateAsync(http.Headers["X-Agent-Token"].ToString(), ct).ConfigureAwait(false);
    if (!auth.IsAuthenticated)
    {
        return Results.Json(new { error = "X-Agent-Token is missing or invalid." }, statusCode: 401);
    }

    var contentType = http.ContentType?.Split(';')[0].Trim() ?? string.Empty;
    if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(
            new { error = $"Content-Type must be application/json or application/octet-stream; got '{http.ContentType}'." },
            statusCode: 415);
    }

    var maxBytes = storageOptions.Value.MaxUploadBytes;
    if (http.ContentLength is { } len && len > maxBytes)
    {
        return Results.Json(
            new { error = $"Upload exceeds MaxUploadBytes ({maxBytes})." },
            statusCode: 413);
    }

    // Read into memory — capped by MaxUploadBytes above. We need the
    // bytes twice (hash + storage write) and Docs.json sizes are bounded.
    byte[] bytes;
    using (var ms = new MemoryStream())
    {
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await http.Body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + read > maxBytes)
            {
                return Results.Json(
                    new { error = $"Upload exceeds MaxUploadBytes ({maxBytes})." },
                    statusCode: 413);
            }
            await ms.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        bytes = ms.ToArray();
    }

    if (bytes.Length == 0)
    {
        return Results.Json(new { error = "Request body is empty." }, statusCode: 400);
    }

    var hashBytes = System.Security.Cryptography.SHA256.HashData(bytes);
    var docsHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

    // If-None-Match short-circuit. We compare against the stored row's
    // current hash; a match means "nothing to do, the agent's already
    // sent this version". Returns 304 with no body.
    var ifNoneMatch = http.Headers.IfNoneMatch.ToString().Trim('"', ' ');
    var existing = await catalogues.GetAsync(auth.PlayerId, PlayerCatalogue.SatisfactoryGame, ct).ConfigureAwait(false);
    if (existing is not null && string.Equals(existing.DocsHash, docsHash, StringComparison.OrdinalIgnoreCase))
    {
        log.LogInformation("Catalogue upload from {PlayerId} matched existing hash {Hash}; 304.", auth.PlayerId, docsHash);
        return Results.StatusCode(304);
    }
    if (existing is not null && !string.IsNullOrEmpty(ifNoneMatch)
        && string.Equals(existing.DocsHash, ifNoneMatch, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.DocsHash, docsHash, StringComparison.OrdinalIgnoreCase))
    {
        // Defensive — covered by the check above, but keeps the
        // If-None-Match semantics explicit if the agent and DB diverge.
        return Results.StatusCode(304);
    }

    var storageKey = await storage.StoreAsync(auth.PlayerId.Value, PlayerCatalogue.SatisfactoryGame, docsHash, bytes, ct)
        .ConfigureAwait(false);
    var now = clock.GetUtcNow().UtcDateTime;

    if (existing is null)
    {
        var row = new PlayerCatalogue(
            auth.PlayerId,
            PlayerCatalogue.SatisfactoryGame,
            docsHash,
            storageKey,
            bytes.Length,
            now);
        await catalogues.AddAsync(row, ct).ConfigureAwait(false);
    }
    else
    {
        existing.ReplaceWith(docsHash, storageKey, bytes.Length, now, gameVersion: null);
    }
    await catalogues.SaveChangesAsync(ct).ConfigureAwait(false);

    // Clear the re-ingest flag now that we've absorbed a fresh upload.
    // Per ADR-0025 §7: any catalogue upload from any of the player's
    // agents counts as "satisfied" — even a 304 would (but 304 paths
    // return above before reaching here, so we don't bother in that
    // branch since nothing changed on disk anyway).
    var player = await players.GetAsync(auth.PlayerId, ct).ConfigureAwait(false);
    if (player is not null && player.ReIngestRequested)
    {
        player.ClearReIngestRequest();
        await players.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    log.LogInformation(
        "Catalogue upload from {PlayerId}: {Bytes} bytes, hash {Hash}, stored at {Key}.",
        auth.PlayerId, bytes.Length, docsHash, storageKey);

    return Results.Ok(new
    {
        docsHash,
        sizeBytes = bytes.Length,
        uploadedUtc = now,
        changed = true,
    });
});

// Player + agent-token endpoints (GET /players/current, POST/GET/DELETE
// /players/{id}/agent-tokens, GET /api/me) moved to Erp.Presentation.Api.Auth
// in phase 5c2 — see ADR-0026 §Presentation/Api/Auth and the auth binary's
// Program.cs.

// ---- Catalogue re-ingest control (ADR-0025 §7) ----------------------------
//
//   POST /players/{id}/re-ingest-catalogue  — set the sticky flag. Picked up
//                                              by the agent on its next
//                                              log-tail poll (~60 s) which
//                                              forces a re-upload regardless
//                                              of the agent's cached hash.
//   GET  /players/{id}/catalogue/satisfactory — current catalogue metadata
//                                                + the re-ingest flag, for
//                                                the Web UI to render.
//
// No caller-side auth on these in v2 (same stance as the token-management
// endpoints — gated by the Web UI being on the homelab-internal LAN).
// ---------------------------------------------------------------------------

app.MapPost("/players/{id:guid}/re-ingest-catalogue", async (
    Guid id,
    IPlayerRepository players,
    TimeProvider clock,
    CancellationToken ct) =>
{
    var playerId = new PlayerId(id);
    var player = await players.GetAsync(playerId, ct).ConfigureAwait(false);
    if (player is null) return Results.NotFound(new { error = $"Player {id} not found." });

    player.RequestReIngest(clock.GetUtcNow().UtcDateTime);
    await players.SaveChangesAsync(ct).ConfigureAwait(false);
    return Results.Accepted(value: new
    {
        reIngestRequested = true,
        reIngestRequestedUtc = player.ReIngestRequestedUtc,
    });
});

app.MapGet("/players/{id:guid}/catalogue/satisfactory", async (
    Guid id,
    IPlayerRepository players,
    IPlayerCatalogueRepository catalogues,
    CancellationToken ct) =>
{
    var playerId = new PlayerId(id);
    var player = await players.GetAsync(playerId, ct).ConfigureAwait(false);
    if (player is null) return Results.NotFound(new { error = $"Player {id} not found." });

    var row = await catalogues.GetAsync(playerId, PlayerCatalogue.SatisfactoryGame, ct).ConfigureAwait(false);
    return Results.Ok(new
    {
        playerId = player.Id.Value,
        reIngestRequested = player.ReIngestRequested,
        reIngestRequestedUtc = player.ReIngestRequestedUtc,
        catalogue = row is null ? null : new
        {
            docsHash = row.DocsHash,
            gameVersion = row.GameVersion,
            sizeBytes = row.SizeBytes,
            uploadedUtc = row.UploadedUtc,
        },
    });
});

app.MapDefaultEndpoints();

app.Run();

internal static class ShareTokenGenerator
{
    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[12];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

public sealed record ShareTokenView(string Token, string Url, DateTime CreatedUtc, DateTime? ExpiresUtc);

/// <summary>POST /api/agent/logs body (#210). Lines are raw text — server doesn't
/// parse Serilog's output template, the Web component highlights levels on
/// best-effort.</summary>
public sealed record AgentLogsRequest(IReadOnlyList<string>? Lines, string? AgentVersion = null);

public partial class Program { }

public sealed record ItemDto(string Id, string Name);

public sealed record RecipeView(
    string Id,
    string Name,
    string BuildingId,
    string BuildingName,
    double BuildingPowerMw,
    bool IsAlternate,
    double DurationSeconds,
    IReadOnlyList<AmountDto> InputsPerMinute,
    IReadOnlyList<AmountDto> OutputsPerMinute);

public sealed record ConfigureCatalogueRequest(string DocsPath);

public sealed record IngestSaveRequest(string SavePath);

/// <summary>
/// PUT /factory/node-override body. Purity arrives as a string (Impure /
/// Normal / Pure) — Minimal APIs JSON binding doesn't string-bind enums by
/// default, and we don't want to enable that globally for everything else.
/// </summary>
public sealed record NodeOverrideRequest(string Reference, string Resource, string Purity);

public sealed record DetectedSaveView(string Path, string Name, DateTime LastWriteTimeUtc, long SizeBytes);

/// <summary>Wire shape for <c>GET /factory/alerts</c> (#116).
/// Severity is the enum name (string) so the JSON reads cleanly and ADA can
/// pattern-match on it without a separate vocabulary mapping.</summary>
public sealed record FactoryAlertView(
    Guid Id,
    string Key,
    string Severity,
    string Source,
    string Title,
    string Detail,
    string Fix,
    DateTime CreatedUtc)
{
    public static FactoryAlertView From(FactoryAlert a) =>
        new(a.Id, a.Key, a.Severity.ToString(), a.Source, a.Title, a.Detail, a.Fix, a.CreatedUtc);
}

/// <summary>One row in the in-app filesystem picker (issue #84).</summary>
public sealed record FsEntryView(string Name, string FullPath, bool IsDirectory, DateTime LastWriteTimeUtc, long? SizeBytes);

/// <summary>Response from GET /fs/browse — what the picker dialog renders for a directory.</summary>
public sealed record FsBrowseView(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<FsEntryView> Directories,
    IReadOnlyList<FsEntryView> Files);

public sealed record SaveMetadataView(
    string SessionName,
    int SaveVersion,
    int BuildVersion,
    double PlayedSeconds,
    DateTime SaveDateTimeUtc);

public sealed record CountView(string Key, int Count);

/// <summary>One row in the "buildings by type × recipe" table on /factory/ingest.</summary>
public sealed record BuildingGroupView(
    string Building,
    string? BuildingName,
    string? Recipe,
    string? RecipeName,
    int Count);

public sealed record FactoryStateView(
    bool IsLoaded,
    string? Source,
    SaveMetadataView? Save,
    IReadOnlyList<CountView> Miners,
    int MinersBoundToNode,
    IReadOnlyList<BuildingGroupView> Buildings,
    int BuildingsWithRecipe,
    IReadOnlyList<CountView> Belts,
    IReadOnlyList<CountView> Pipelines,
    IReadOnlyList<CountView> Generators,
    int ResourceNodeCount,
    IReadOnlyList<string> Warnings)
{
    public static FactoryStateView From(IFactoryStateProvider provider, ICatalogProvider catalog)
    {
        var state = provider.Current;
        var meta = provider.IsLoaded
            ? new SaveMetadataView(
                state.Save.SessionName,
                state.Save.SaveVersion,
                state.Save.BuildVersion,
                state.Save.PlayedTime.TotalSeconds,
                state.Save.SaveDateTimeUtc)
            : null;

        return new FactoryStateView(
            IsLoaded: provider.IsLoaded,
            Source: provider.Source,
            Save: meta,
            Miners: state.Miners
                .GroupBy(m => m.Tier.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            MinersBoundToNode: state.Miners.Count(m => !string.IsNullOrEmpty(m.ResourceNodeReference)),
            Buildings: state.Buildings
                .GroupBy(b => (Building: b.Building.Value, Recipe: b.Recipe?.Value))
                .Select(g => new BuildingGroupView(
                    Building: g.Key.Building,
                    BuildingName: catalog.FindBuilding(new BuildingId(g.Key.Building))?.Name,
                    Recipe: g.Key.Recipe,
                    RecipeName: g.Key.Recipe is { Length: > 0 } r
                        ? catalog.FindRecipe(new RecipeId(r))?.Name
                        : null,
                    Count: g.Count()))
                .OrderBy(b => b.Building, StringComparer.Ordinal)
                .ThenByDescending(b => b.Count)
                .ToList(),
            BuildingsWithRecipe: state.Buildings.Count(b => b.Recipe is not null),
            Belts: state.Belts
                .GroupBy(b => b.Tier.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            Pipelines: state.Pipelines
                .GroupBy(p => p.Tier.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            Generators: state.Generators
                .GroupBy(g => g.Kind.ToString())
                .Select(g => new CountView(g.Key, g.Count()))
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .ToList(),
            ResourceNodeCount: state.ResourceNodes.Count,
            Warnings: state.Warnings);
    }
}

// ---------------------------------------------------------------------------
// GeoJSON projection for the map page (ADR-0013).
// FeatureCollection with one Feature per parsed entity. Coordinates are raw
// Unreal world X/Y in centimetres — the JS layer (using Leaflet's CRS.Simple)
// handles axis orientation + zoom bounds.
// ---------------------------------------------------------------------------

// GeoJSON geometry — Point uses [x, y]; LineString uses [[x, y], …]. We
// serialise both shapes through `object` so the JSON layout matches the
// GeoJSON spec without needing two parallel `GeoFeature` records.
public sealed record GeoGeometry(string Type, object Coordinates)
{
    public static GeoGeometry Point(Position p) => new("Point", new double[] { p.X, p.Y });

    public static GeoGeometry LineString(IReadOnlyList<Position> polyline)
    {
        var coords = new double[polyline.Count][];
        for (var i = 0; i < polyline.Count; i++)
            coords[i] = [polyline[i].X, polyline[i].Y];
        return new GeoGeometry("LineString", coords);
    }
}

public sealed record GeoFeature(
    string Type,
    GeoGeometry Geometry,
    Dictionary<string, object?> Properties)
{
    public static GeoFeature Make(string category, string kind, Position position, Dictionary<string, object?>? extra = null)
        => new("Feature", GeoGeometry.Point(position), BuildProps(category, kind, position, extra));

    public static GeoFeature MakeLine(string category, string kind, IReadOnlyList<Position> polyline, Position fallback, Dictionary<string, object?>? extra = null)
        => new("Feature", GeoGeometry.LineString(polyline), BuildProps(category, kind, fallback, extra));

    private static Dictionary<string, object?> BuildProps(string category, string kind, Position position, Dictionary<string, object?>? extra)
    {
        var props = new Dictionary<string, object?>
        {
            ["category"] = category,
            ["kind"] = kind,
            ["z"] = position.Z,
        };
        if (extra is not null)
            foreach (var kv in extra) props[kv.Key] = kv.Value;
        return props;
    }
}

public sealed record FactoryStateGeoJson(
    string Type,
    IReadOnlyList<GeoFeature> Features,
    Dictionary<string, object?> Metadata)
{
    public static FactoryStateGeoJson From(IFactoryStateProvider provider, ICatalogProvider catalog)
        => From(provider, catalog, Satisfactory.Infrastructure.KnownFlora.Empty);

    public static FactoryStateGeoJson From(
        IFactoryStateProvider provider,
        ICatalogProvider catalog,
        Satisfactory.Infrastructure.KnownFlora flora)
    {
        var s = provider.Current;
        var features = new List<GeoFeature>();

        foreach (var n in s.ResourceNodes)
        {
            // #125 — Deposits (small destructible scenery piles, ~hundreds per
            // save) get their own category so the map can hide them by default
            // via a layer toggle. Mining nodes / geysers / fracking cores stay
            // under `resource-node` (visible by default).
            var category = n.Kind == ResourceNodeKind.Deposit
                ? "resource-deposit"
                : "resource-node";
            features.Add(GeoFeature.Make(category, n.Reference, n.Position, new()
            {
                ["nodeKind"] = n.Kind.ToString(),
                ["purity"] = n.Purity.ToString(),
                ["resource"] = n.Resource?.Value,
                ["resourceName"] = n.Resource is { Value: { Length: > 0 } } id
                    ? catalog.FindItem(id)?.Name
                    : null,
            }));
        }

        foreach (var m in s.Miners)
            features.Add(GeoFeature.Make("miner", m.Reference, m.Position, new()
            {
                ["tier"] = m.Tier.ToString(),
                ["resourceNode"] = m.ResourceNodeReference,
            }));

        foreach (var b in s.Buildings)
            features.Add(GeoFeature.Make("building", b.Building.Value, b.Position, new()
            {
                ["buildingName"] = catalog.FindBuilding(b.Building)?.Name,
                ["recipe"] = b.Recipe?.Value,
                ["recipeName"] = b.Recipe is { Value: { Length: > 0 } } id
                    ? catalog.FindRecipe(id)?.Name
                    : null,
                ["clockSpeed"] = b.ClockSpeed,
            }));

        foreach (var belt in s.Belts)
        {
            var props = new Dictionary<string, object?>
            {
                ["tier"] = belt.Tier.ToString(),
            };
            if (belt.Polyline is { Count: >= 2 } poly)
                features.Add(GeoFeature.MakeLine("belt", belt.Reference, poly, belt.Position, props));
            else
                features.Add(GeoFeature.Make("belt", belt.Reference, belt.Position, props));
        }

        // Pipelines mirror the belt shape exactly — LineString when the
        // polyline has ≥2 points, point fallback otherwise. Polylines will
        // stay empty until the SatisfactorySaveNet fork parses pipe
        // `mSplineData` (Array<Struct<FSplinePointData>>); see issue #65.
        foreach (var pipe in s.Pipelines)
        {
            var props = new Dictionary<string, object?>
            {
                ["tier"] = pipe.Tier.ToString(),
            };
            if (pipe.Polyline is { Count: >= 2 } poly)
                features.Add(GeoFeature.MakeLine("pipe", pipe.Reference, poly, pipe.Position, props));
            else
                features.Add(GeoFeature.Make("pipe", pipe.Reference, pipe.Position, props));
        }

        foreach (var g in s.Generators)
            features.Add(GeoFeature.Make("generator", g.Reference, g.Position, new()
            {
                ["genKind"] = g.Kind.ToString(),
            }));

        // Flora layer (#62) — static dataset, not from the save. Each feature
        // carries the species ItemId so the JS layer can pick the right wiki
        // item icon (Desc_Berry_C.png etc.) and surface a friendly name.
        foreach (var f in flora.All)
        {
            features.Add(GeoFeature.Make("flora", f.Species, new Position(f.X, f.Y, f.Z), new()
            {
                ["species"] = f.Species,
                ["speciesName"] = f.DisplayName,
            }));
        }

        var meta = new Dictionary<string, object?>
        {
            ["isLoaded"] = provider.IsLoaded,
            ["source"] = provider.Source,
            ["sessionName"] = provider.IsLoaded ? s.Save.SessionName : null,
            ["featureCount"] = features.Count,
        };

        return new FactoryStateGeoJson("FeatureCollection", features, meta);
    }
}

public sealed record TargetDto(string ItemId, decimal ItemsPerMinute);
public sealed record AvailabilityDto(string ItemId, decimal ItemsPerMinute);

/// <summary>Node binding for <see cref="PlanRequest"/> (#92). <c>Purity</c>
/// and <c>AvailableTiers</c> are case-insensitive enum names
/// ("Impure"/"Normal"/"Pure", "Mk1"/"Mk2"/"Mk3") so the JSON stays
/// readable. <c>AvailableTiers</c> empty/null means all three.</summary>
public sealed record NodeAvailabilityDto(
    string NodeReference,
    string Resource,
    string Purity,
    IReadOnlyList<string>? AvailableTiers);

/// <summary>Per-node extraction breakdown returned in <see cref="PlanDto"/>
/// when the request included <see cref="PlanRequest.Nodes"/>.</summary>
public sealed record ExtractorAllocationDto(
    string NodeReference,
    string Resource,
    string ResourceName,
    string Purity,
    string Tier,
    decimal MinerFraction,
    decimal OutputPerMinute);

public sealed record PlanRequest(
    IReadOnlyList<TargetDto> Targets,
    IReadOnlyList<AvailabilityDto> Available,
    IReadOnlyList<NodeAvailabilityDto>? Nodes = null,
    decimal? PowerTargetMw = null);

/// <summary>Per-generator power-production row in the plan output (#137).
/// <c>Kind</c> + <c>Tier</c>-style string for clean JSON. <c>Fuel</c> is the
/// item id; <c>FuelName</c> is the catalog display name.</summary>
public sealed record GeneratorAllocationDto(
    string Kind,
    string Fuel,
    string FuelName,
    decimal BuildingCount,
    decimal PowerMw);

public sealed record AmountDto(string ItemId, string ItemName, decimal ItemsPerMinute);
public sealed record StepDto(
    string RecipeId,
    string RecipeName,
    string BuildingId,
    string BuildingName,
    decimal BuildingCount,
    decimal PowerMw,
    IReadOnlyList<AmountDto> Inputs,
    IReadOnlyList<AmountDto> Outputs);

// ---- Saved plan DTOs (issue #77) -------------------------------------------
// Wire shapes for /plans endpoints. Kept thin and string-typed so the Web
// client doesn't take a reference on the Erp.Domain.Common assembly.

public sealed record SavePlanRequest(
    string Name,
    IReadOnlyList<TargetDto>? Targets,
    IReadOnlyList<AvailabilityDto>? Available);

public sealed record SavedPlanSummaryDto(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    int TargetCount,
    int AvailableCount)
{
    public static SavedPlanSummaryDto From(SavedPlan p) =>
        new(p.Id, p.Name, p.CreatedUtc, p.UpdatedUtc, p.Targets.Count, p.Available.Count);
}

public sealed record SavedPlanDto(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<TargetDto> Targets,
    IReadOnlyList<AvailabilityDto> Available)
{
    public static SavedPlanDto From(SavedPlan p) =>
        new(p.Id, p.Name, p.CreatedUtc, p.UpdatedUtc,
            p.Targets.Select(t => new TargetDto(t.Item.Value, t.ItemsPerMinute)).ToList(),
            p.Available.Select(a => new AvailabilityDto(a.Item.Value, a.ItemsPerMinute)).ToList());
}

/// <summary>Diagnostic entry for an item the planner couldn't supply (#8).
/// Existing <c>ItemId</c>, <c>ItemName</c>, <c>ItemsPerMinute</c> fields kept
/// so older JSON readers keep working; new <c>Reason</c> + alternate-recipe
/// + top-consumer arrays surface the actionable info.</summary>
public sealed record MissingInputDto(
    string ItemId,
    string ItemName,
    decimal ItemsPerMinute,
    string Reason,
    IReadOnlyList<RecipeRefDto> CouldBeProducedBy,
    IReadOnlyList<RecipeRefDto> TopConsumers);

public sealed record RecipeRefDto(string Id, string Name);

/// <summary>Per-fluid pipe-throughput summary (#90). <c>RecommendedTier</c>
/// is the enum name ("Mk1" / "Mk2" / "OverMk2") for clean JSON.</summary>
public sealed record FluidPipeRequirementDto(
    string ItemId,
    string ItemName,
    decimal MaxRatePerMinute,
    string RecommendedTier);

/// <summary>LP sensitivity surface attached to <see cref="PlanDto"/> when
/// the OR-Tools engine ran the plan (#129). <c>null</c> on plans produced
/// by the recursive engine.</summary>
public sealed record LpSensitivityDto(
    IReadOnlyList<ItemShadowPriceDto> SupplyConstraints,
    IReadOnlyList<RecipeReducedCostDto> ProductionRecipes);

public sealed record ItemShadowPriceDto(
    string ItemId,
    string ItemName,
    decimal ShadowPrice,
    decimal Slack);

public sealed record RecipeReducedCostDto(
    string RecipeId,
    string RecipeName,
    decimal ReducedCost);

public sealed record PlanDto(
    bool IsFeasible,
    IReadOnlyList<StepDto> Steps,
    decimal TotalPowerMw,
    IReadOnlyList<AmountDto> RawInputsConsumed,
    IReadOnlyList<MissingInputDto> MissingInputs,
    IReadOnlyList<ExtractorAllocationDto> ExtractorAllocations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<FluidPipeRequirementDto> FluidPipeRequirements,
    LpSensitivityDto? Sensitivity,
    IReadOnlyList<GeneratorAllocationDto> GeneratorAllocations)
{
    public static PlanDto From(ProductionPlan plan, ICatalogProvider catalog)
    {
        AmountDto ToAmount(ItemAmount a) =>
            new(a.Item.Value, catalog.FindItem(a.Item)?.Name ?? a.Item.Value, Math.Round(a.Quantity, 4));

        RecipeRefDto ToRecipeRef(RecipeId id) =>
            new(id.Value, catalog.FindRecipe(id)?.Name ?? id.Value);

        MissingInputDto ToMissing(InfeasibleItem m) =>
            new(
                ItemId: m.Item.Value,
                ItemName: catalog.FindItem(m.Item)?.Name ?? m.Item.Value,
                ItemsPerMinute: Math.Round(m.QuantityShort, 4),
                Reason: m.Reason,
                CouldBeProducedBy: m.CouldBeProducedBy.Select(ToRecipeRef).ToList(),
                TopConsumers: m.TopConsumers.Select(ToRecipeRef).ToList());

        ExtractorAllocationDto ToAllocation(ExtractorAllocation a) =>
            new(
                NodeReference: a.NodeReference,
                Resource: a.Resource.Value,
                ResourceName: catalog.FindItem(a.Resource)?.Name ?? a.Resource.Value,
                Purity: a.Purity.ToString(),
                Tier: a.Tier.ToString(),
                MinerFraction: Math.Round(a.MinerFraction, 4),
                OutputPerMinute: Math.Round(a.OutputPerMinute, 4));

        FluidPipeRequirementDto ToFluidPipe(FluidPipeRequirement f) =>
            new(
                ItemId: f.Item.Value,
                ItemName: catalog.FindItem(f.Item)?.Name ?? f.Item.Value,
                MaxRatePerMinute: Math.Round(f.MaxRatePerMinute, 4),
                RecommendedTier: f.RecommendedTier.ToString());

        LpSensitivityDto? ToSensitivity(LpSensitivity? s)
        {
            if (s is null) return null;
            return new LpSensitivityDto(
                SupplyConstraints: s.SupplyConstraints
                    .Select(sp => new ItemShadowPriceDto(
                        ItemId: sp.Item.Value,
                        ItemName: catalog.FindItem(sp.Item)?.Name ?? sp.Item.Value,
                        ShadowPrice: Math.Round(sp.ShadowPrice, 6),
                        Slack: Math.Round(sp.Slack, 4)))
                    .ToList(),
                ProductionRecipes: s.ProductionRecipes
                    .Select(rc => new RecipeReducedCostDto(
                        RecipeId: rc.Recipe.Value,
                        RecipeName: catalog.FindRecipe(rc.Recipe)?.Name ?? rc.Recipe.Value,
                        ReducedCost: Math.Round(rc.ReducedCost, 6)))
                    .ToList());
        }

        var steps = plan.Steps.Select(s => new StepDto(
            s.Recipe.Id.Value,
            s.Recipe.Name,
            s.Recipe.Building.Value,
            catalog.FindBuilding(s.Recipe.Building)?.Name ?? s.Recipe.Building.Value,
            Math.Round(s.BuildingCount, 4),
            Math.Round(s.PowerMw, 4),
            s.InputsPerMinute.Select(ToAmount).ToList(),
            s.OutputsPerMinute.Select(ToAmount).ToList())).ToList();

        return new(
            IsFeasible: plan.IsFeasible,
            Steps: steps,
            TotalPowerMw: Math.Round(steps.Sum(s => s.PowerMw), 4),
            RawInputsConsumed: plan.RawInputsConsumed.Select(ToAmount).ToList(),
            MissingInputs: plan.MissingInputs.Select(ToMissing).ToList(),
            ExtractorAllocations: plan.Allocations.Select(ToAllocation).ToList(),
            Warnings: plan.WarningsOrEmpty,
            FluidPipeRequirements: plan.Pipes.Select(ToFluidPipe).ToList(),
            Sensitivity: ToSensitivity(plan.Sensitivity),
            GeneratorAllocations: plan.Generators.Select(g => new GeneratorAllocationDto(
                Kind: g.Kind.ToString(),
                Fuel: g.Fuel.Value,
                FuelName: catalog.FindItem(g.Fuel)?.Name ?? g.Fuel.Value,
                BuildingCount: Math.Round(g.BuildingCount, 4),
                PowerMw: Math.Round(g.PowerMw, 4))).ToList());
    }
}
