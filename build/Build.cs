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

    Target Test => _ => _
        .Description("Runs all xUnit test projects, emits TRX results to artifacts/test-results/.")
        .DependsOn(Compile)
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

    Target ComputeVersion => _ => _
        .Description("Reads the Nerdbank.GitVersioning version (SemVer2) and prints it.")
        .Executes(() =>
        {
            var version = ReadNbgvVersion();
            Log.Information("Computed version: {Version}", version);
        });

    Target Release => _ => _
        .Description("Creates a GitHub release for the current commit. Auto-runs on push to main.")
        .DependsOn(Test)
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
