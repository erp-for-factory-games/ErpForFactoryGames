using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// ReSharper disable AllUnderscoreLocalParameterName

/// <summary>
/// ERP.Satisfactory build pipeline.
///
/// Local usage:
///   ./build.ps1               (default target — Compile)
///   ./build.sh Test           (runs Clean → Restore → Compile → Test)
///   ./build.cmd Format        (verifies dotnet format passes; excludes vendor/)
///
/// CI invokes the same entry points (see .github/workflows/ci.yml). Logic
/// lives here in C# rather than YAML so it runs identically locally.
/// </summary>
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build — Debug locally, Release on CI.")]
    readonly Configuration Configuration = IsServerBuild ? Configuration.Release : Configuration.Debug;

    [Solution(GenerateProjects = true)]
    readonly Solution Solution = null!;

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

            var persistenceProject = RootDirectory / "src" / "ERP" / "Infrastructure" / "Persistence";
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

            var persistenceProject = RootDirectory / "src" / "ERP" / "Infrastructure" / "Persistence";
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
