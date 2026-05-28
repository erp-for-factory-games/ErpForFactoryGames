using System.Text.Json;
using Erp.Deploy;
using Erp.Deploy.Configuration;
using Erp.Deploy.Ssh;
using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.IO;
using Fallout.Common.ProjectModel;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Serilog;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

/// <summary>
/// ErpForFactoryGames build pipeline.
///
/// Local usage:
///   ./build.ps1               (default target — Compile)
///   ./build.sh Test           (runs Clean → Restore → Compile → Test)
///   ./build.cmd Format        (verifies dotnet format passes; excludes vendor/)
///
/// CI invokes the same entry points (see .github/workflows/ci.yml). Logic
/// lives here in C# rather than YAML so it runs identically locally.
/// </summary>
class Build : FalloutBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build — Debug locally, Release on CI.")]
    readonly Configuration Configuration = IsServerBuild ? Configuration.Release : Configuration.Debug;

    [Solution(GenerateProjects = true)]
    readonly Solution Solution = null!;

    // -------------------------------------------------------------------------
    // Deploy parameters (consumed by Provision target).
    //
    // CloudflareApiToken: [Secret] makes Fallout prompt for it (masked) when
    // not supplied via env var or command line, and keeps it out of logs.
    // Locally we expect `CLOUDFLARE_API_TOKEN=$(bw get password '<vault-item>')`
    // before ./build.sh Provision; in CI it comes from GitHub Actions secrets.
    // -------------------------------------------------------------------------
    [Parameter("Cloudflare API token. Scopes: Account · Cloudflare Tunnel · Edit; Zone · DNS · Edit; Account · Account Settings · Read.")]
    [Secret]
    readonly string CloudflareApiToken = null!;

    [Parameter("Plan changes without writing them back to Cloudflare (Provision) or the LXC (Up).")]
    readonly bool DryRun;

    [Parameter("Output format for Provision: text (human, default) or json.")]
    readonly string DeployOutput = "text";

    [Parameter("Image tag for erp-web + erp-api. Default: latest.")]
    readonly string ImageTag = "latest";

    // Populated by Provision when it applies; read by Up. Empty when Provision
    // ran in dry-run, in which case Up uses a placeholder token for its own
    // dry-run output and refuses to proceed if asked to apply for real.
    IReadOnlyList<TunnelOutput> _connectorTokens = Array.Empty<TunnelOutput>();

    GitHubActions GitHubActions => GitHubActions.Instance;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";

    Target Clean => _ => _
        .Description("Clears the artifacts directory.")
        .Before(Restore)
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Description("Restores NuGet packages for the solution.")
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .Description("Builds the solution.")
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Format => _ => _
        .Description("Verifies dotnet format passes (read-only; CI gate). Vendor submodule excluded.")
        .Executes(() =>
        {
            DotNet($"format \"{Solution.Path}\" --verify-no-changes --exclude vendor/");
        });

    Target InstallPlaywrightBrowsers => _ => _
        .Description("Installs Playwright browsers used by Web.UiTests. Idempotent — no-op if already cached.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            // playwright.ps1 is emitted into the test project's output by Microsoft.Playwright.
            var script = RootDirectory / "test" / "Web" / "Web.UiTests" / "bin" / Configuration / "net10.0" / "playwright.ps1";
            if (!script.FileExists())
            {
                Log.Warning("playwright.ps1 not found at {Script} — skipping browser install. Did the test project build?", script);
                return;
            }

            // --with-deps installs Linux system libs (libnss3 etc) needed by chromium; needs sudo on CI.
            // On Windows/Mac the deps come from the OS — plain `install` is enough.
            var args = EnvironmentInfo.IsLinux ? $"\"{script}\" install --with-deps chromium" : $"\"{script}\" install chromium";
            var process = ProcessTasks.StartProcess("pwsh", args, workingDirectory: RootDirectory);
            process.AssertZeroExitCode();
        });

    Target Test => _ => _
        .Description("Runs ALL xUnit test projects including Web.UiTests (drives Chromium via Playwright). Use locally; CI uses TestNoUi instead.")
        .DependsOn(Compile)
        .DependsOn(InstallPlaywrightBrowsers)
        .Executes(() =>
        {
            TestResultsDirectory.CreateOrCleanDirectory();
            DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetResultsDirectory(TestResultsDirectory)
                .AddLoggers("trx;LogFilePrefix=test")
                .AddLoggers("console;verbosity=normal"));
            Log.Information("TRX results written to {Dir}", TestResultsDirectory);
        });

    Target TestNoUi => _ => _
        .Description("Runs xUnit tests excluding Web.UiTests. Skips the Playwright browser install + system-deps that the free GitHub Actions runner can't comfortably carry.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            TestResultsDirectory.CreateOrCleanDirectory();
            // Filter by FullyQualifiedName — every test in Web.UiTests lives under the
            // Web.UiTests namespace, so !~ Web.UiTests matches them all. The UiTests
            // project still compiles but no tests run, so the AspireAppFixture (which
            // calls Microsoft.Playwright.Program.Main on init) never executes.
            DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetResultsDirectory(TestResultsDirectory)
                .AddLoggers("trx;LogFilePrefix=test")
                .AddLoggers("console;verbosity=normal")
                .SetFilter("FullyQualifiedName!~Web.UiTests"));
            Log.Information("TRX results written to {Dir} (Web.UiTests excluded — run `./build.sh Test` locally to include them)", TestResultsDirectory);
        });

    Target TestUi => _ => _
        .Description("Runs ONLY Web.UiTests (the Playwright slice). Mirror of TestNoUi — used by the label-gated ui-tests CI workflow (#128).")
        .DependsOn(Compile)
        .DependsOn(InstallPlaywrightBrowsers)
        .Executes(() =>
        {
            TestResultsDirectory.CreateOrCleanDirectory();
            // Inverted filter from TestNoUi — only tests whose FQN contains
            // Web.UiTests run. The rest of the suite is skipped.
            DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetResultsDirectory(TestResultsDirectory)
                .AddLoggers("trx;LogFilePrefix=test-ui")
                .AddLoggers("console;verbosity=normal")
                .SetFilter("FullyQualifiedName~Web.UiTests"));
            Log.Information("TRX results written to {Dir} (Web.UiTests only)", TestResultsDirectory);
        });

    // -------------------------------------------------------------------------
    // Migration-drift guard (ADR-0018 follow-up, issue #81).
    //
    // Dual-provider EF Core means two migration sets that must each match the
    // current `PlanDbContext` model. `dotnet ef migrations has-pending-model-changes`
    // exits non-zero when the snapshot is out of date — perfect for CI.
    //
    // Runs for both SqlitePlanDbContext and PostgresPlanDbContext. The Postgres
    // design-time factory requires a connection-string env var (the connection
    // isn't actually opened for `has-pending-model-changes`, but the factory
    // refuses to construct without it).
    // -------------------------------------------------------------------------
    Target CheckMigrations => _ => _
        .Description("Verifies both EF Core migration sets (SQLite + Postgres) are in sync with the current model.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Tool restore is idempotent — pulls dotnet-ef from .config/dotnet-tools.json.
            DotNet("tool restore", workingDirectory: RootDirectory, logOutput: false);

            var persistenceProject = RootDirectory / "src" / "Infrastructure" / "Persistence" / "Erp.Infrastructure.Persistence";
            var startupProject = RootDirectory / "src" / "ApiService";

            void Check(string contextName, params (string Key, string Value)[] extraEnv)
            {
                Log.Information("Checking pending model changes for {Context}", contextName);
                // --configuration matches what Compile produced so --no-build can find
                // the right bin/<Configuration>/net10.0/ApiService.deps.json. Without
                // this, ef defaults to Debug while CI builds Release and the lookup fails.
                var args = $"ef migrations has-pending-model-changes " +
                           $"--project \"{persistenceProject}\" " +
                           $"--startup-project \"{startupProject}\" " +
                           $"--context {contextName} " +
                           $"--configuration {Configuration} " +
                           $"--no-build";

                var env = new Dictionary<string, string>(EnvironmentInfo.Variables);
                foreach (var (k, v) in extraEnv) env[k] = v;

                var process = ProcessTasks.StartProcess(
                    "dotnet",
                    args,
                    workingDirectory: RootDirectory,
                    environmentVariables: env);
                process.AssertZeroExitCode();
                Log.Information("{Context}: migration snapshot is in sync.", contextName);
            }

            Check("SqlitePlanDbContext");

            // Placeholder connection string — design-time factory only validates
            // that it's set, it never opens the connection for this command.
            Check("PostgresPlanDbContext",
                ("ERP_PERSISTENCE_CONNECTION", "Host=localhost;Database=plans;Username=postgres;Password=placeholder"));
        });

    // -------------------------------------------------------------------------
    // Postgres runtime smoke (ADR-0018 follow-up, issue #81).
    //
    // Applies the Postgres migration set against a real Postgres instance and
    // asserts the schema creates successfully. In CI this runs inside a job
    // with a `postgres:16` services container; locally you point it at any
    // reachable Postgres via the standard env vars.
    //
    // Requires:
    //   ERP_PERSISTENCE_CONNECTION = Npgsql connection string to a live DB.
    // -------------------------------------------------------------------------
    Target MigrationsPostgresSmoke => _ => _
        .Description("Applies the Postgres migration set against a live database. Requires ERP_PERSISTENCE_CONNECTION.")
        .DependsOn(Compile)
        .Executes(() =>
        {
            var conn = Environment.GetEnvironmentVariable("ERP_PERSISTENCE_CONNECTION");
            if (string.IsNullOrWhiteSpace(conn))
            {
                throw new InvalidOperationException(
                    "MigrationsPostgresSmoke requires ERP_PERSISTENCE_CONNECTION pointing at a live Postgres. " +
                    "Locally: `docker run --rm -d -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16` then " +
                    "set ERP_PERSISTENCE_CONNECTION=Host=localhost;Database=plans;Username=postgres;Password=postgres.");
            }

            DotNet("tool restore", workingDirectory: RootDirectory, logOutput: false);

            var persistenceProject = RootDirectory / "src" / "Infrastructure" / "Persistence" / "Erp.Infrastructure.Persistence";
            var startupProject = RootDirectory / "src" / "ApiService";

            var env = new Dictionary<string, string>(EnvironmentInfo.Variables)
            {
                ["ERP_PERSISTENCE_CONNECTION"] = conn,
            };

            var args = $"ef database update " +
                       $"--project \"{persistenceProject}\" " +
                       $"--startup-project \"{startupProject}\" " +
                       $"--context PostgresPlanDbContext " +
                       $"--configuration {Configuration} " +
                       $"--no-build";

            var process = ProcessTasks.StartProcess(
                "dotnet",
                args,
                workingDirectory: RootDirectory,
                environmentVariables: env);
            process.AssertZeroExitCode();
            Log.Information("Postgres migration applied successfully against {Conn}",
                System.Text.RegularExpressions.Regex.Replace(conn, "Password=[^;]*", "Password=***"));
        });

    Target ComputeVersion => _ => _
        .Description("Reads the Nerdbank.GitVersioning version (SemVer2) and prints it.")
        .Executes(() =>
        {
            var version = ReadNbgvVersion();
            Log.Information("Computed version: {Version}", version);
        });

    Target Release => _ => _
        .Description("Creates a GitHub release for the current commit. Auto-runs on push to main.")
        // No test dep — CI's release job is gated on the `build-and-test`,
        // `lint`, `migration-drift`, and `postgres-smoke` jobs via `needs:`,
        // so the tests already ran. Depending on `Test` here would
        // redundantly install Playwright + Chromium on the release runner
        // and hang on the host-deps step. Re-add a `DependsOn(TestNoUi)`
        // if/when we want this target to be safe to invoke locally.
        // Backlog issue tracks running UI tests in CI proper.
        .DependsOn(Compile)
        .OnlyWhenStatic(() => IsServerBuild
                              && GitHubActions is not null
                              && GitHubActions.Ref == "refs/heads/main"
                              && GitHubActions.EventName == "push")
        .Executes(() =>
        {
            var version = ReadNbgvVersion();
            var tag = $"v{version}";
            var sha = GitHubActions!.Sha;
            Log.Information("Creating GitHub release {Tag} for {Sha}", tag, sha);

            // `gh release create` with --generate-notes auto-builds the
            // notes from commits since the previous tag. --target pins
            // the release to the exact commit we tested.
            var process = ProcessTasks.StartProcess(
                "gh",
                $"release create {tag} --title \"Release {tag}\" --generate-notes --target {sha}",
                workingDirectory: RootDirectory);
            process.AssertZeroExitCode();
        });

    // -------------------------------------------------------------------------
    // Deploy: reconcile Cloudflare tunnel + ingress + DNS for the production
    // stack. Consumes deploy/erp-deploy.json. POC for Fallout's deploy-agent
    // direction — same C#-as-build-script story extended from CI to CD (#263).
    // -------------------------------------------------------------------------
    Target Provision => _ => _
        .Description("Reconcile Cloudflare tunnel + ingress + DNS for the deploy stack. Pass --dry-run to plan without writes.")
        .Requires(() => CloudflareApiToken)
        .Executes(async () =>
        {
            var configPath = RootDirectory / "deploy" / "erp-deploy.json";
            if (!configPath.FileExists())
            {
                throw new InvalidOperationException($"Deploy config not found at {configPath}");
            }

            var json = configPath.ReadAllText();
            var options = JsonSerializer.Deserialize<DeployOptions>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException($"Failed to parse {configPath}");

            var output = string.Equals(DeployOutput, "json", StringComparison.OrdinalIgnoreCase)
                ? OutputFormat.Json
                : OutputFormat.Text;

            var provisioner = Provisioner.Create(CloudflareApiToken);
            var result = await provisioner.RunAsync(new ProvisionRequest(options, DryRun, output));

            if (result.ExitCode != 0)
            {
                throw new Exception($"Provision failed with exit code {result.ExitCode}.");
            }
            _connectorTokens = result.Tunnels;
            Log.Information("Provision complete ({TunnelCount} tunnel(s), DryRun={DryRun}).", result.Tunnels.Count, DryRun);
        });

    // -------------------------------------------------------------------------
    // Up: ship the compose stack to the LXC and bring it forward.
    //
    // Replaces deploy.ps1 lines 117–183. Uses Renci.SshNet for SFTP + remote
    // exec so the stack.env body is written as raw bytes — no remote shell
    // ever re-parses the connector token line, which is the bug deploy.ps1
    // couldn't shake.
    //
    // DependsOn(Provision): every Up runs the Cloudflare reconcile first so
    // the connector token is fresh. Idempotent for both halves.
    // -------------------------------------------------------------------------
    Target Up => _ => _
        .Description("Ship compose.yml + ingress.json + stack.env to the LXC and `docker compose up -d`. DependsOn(Provision).")
        .DependsOn(Provision)
        .Executes(() =>
        {
            var configPath = RootDirectory / "deploy" / "erp-deploy.json";
            var options = JsonSerializer.Deserialize<DeployOptions>(
                configPath.ReadAllText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            // The compose stack lives in the sister-repo submodule.
            var composeSource = RootDirectory / "deploy" / "Homelab.Stacks.ErpForFactoryGames";

            string token;
            if (DryRun)
            {
                // Provision ran in dry-run too, so there's no real token.
                token = "<dry-run-placeholder>";
            }
            else
            {
                if (_connectorTokens.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No connector token from Provision — did Provision actually apply? " +
                        "If running Up standalone (not via DependsOn), set --dry-run first to verify, " +
                        "then full apply.");
                }
                // Single-tunnel today; if/when multi-tunnel lands, deploy
                // needs to know which token to write into stack.env.
                token = _connectorTokens[0].ConnectorToken;
            }

            var deployer = Deployer.Create();
            var result = deployer.Run(new DeployRequest(
                Options:          options,
                ConnectorToken:   token,
                ImageTag:         ImageTag,
                ComposeSourceDir: composeSource,
                DryRun:           DryRun));

            if (result.ExitCode != 0)
            {
                throw new Exception($"Up failed with exit code {result.ExitCode}.");
            }
            Log.Information("Up complete (ImageTag={ImageTag}, DryRun={DryRun}).", ImageTag, DryRun);
        });

    /// <summary>
    /// Reads the SemVer2 version from `dotnet nbgv` (a local tool restored
    /// from .config/dotnet-tools.json). Returns e.g. "0.1.5" or "0.1.5+abcd".
    /// </summary>
    static string ReadNbgvVersion()
    {
        // Tool restore is idempotent — safe to call every time.
        DotNet("tool restore", workingDirectory: RootDirectory, logOutput: false);

        var result = DotNet("nbgv get-version -v SemVer2",
            workingDirectory: RootDirectory,
            logOutput: false);
        return result.Single().Text.Trim();
    }
}
