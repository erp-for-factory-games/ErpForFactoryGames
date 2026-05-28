using Erp.Presentation.Agent.Common;
using Satisfactory.Presentation.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

// ---------------------------------------------------------------------
// CLI entry — handle --install / --uninstall / --setup / --pair / --help
// before any host machinery boots. These flags do one thing and exit;
// the long-running service path is the default when no flag is given.
//
// Pairing dispatch (ADR-0025 §8):
//   • erp-agent --pair erp-agent://pair?token=...&api=...
//   • erp-agent erp-agent://pair?token=...&api=...    (raw URL via protocol handler)
//   • erp-agent --setup [--token X] [--api Y] [--save-folder Z]
// Both paths converge on PairingService.
// ---------------------------------------------------------------------
for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    switch (arg)
    {
        case "--install":
            return await ServiceRegistrar.InstallAsync().ConfigureAwait(false);
        case "--uninstall":
            return await ServiceRegistrar.UninstallAsync().ConfigureAwait(false);
        case "--version" or "-v":
            Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
            return 0;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        case "--pair":
            {
                var url = (i + 1 < args.Length) ? args[i + 1] : null;
                return await RunPairAsync(url).ConfigureAwait(false);
            }
        case "--setup":
            return await RunSetupAsync(args).ConfigureAwait(false);
    }

    // Protocol handler invocation: Windows / Linux pass the deep-link as
    // the first positional argv when launching us via erp-agent://.
    if (arg.StartsWith($"{PairingUrlParser.Scheme}://", StringComparison.OrdinalIgnoreCase))
    {
        return await RunPairAsync(arg).ConfigureAwait(false);
    }
}

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------
// Service-host shims — see ADR-0024 §1.
// Both calls are no-ops when not launched as the matching service type,
// so the same binary works for `dotnet run`, Windows-service, and systemd.
// ---------------------------------------------------------------------
builder.Services.AddWindowsService(o => o.ServiceName = "ERP for Factory Games — Agent");
builder.Services.AddSystemd();

// ---------------------------------------------------------------------
// Configuration — picks up appsettings.json next to the binary plus the
// shared agent.json (writeable; the install flow drops the seed there)
// plus env vars (ERP_AGENT_*). User-secrets only outside Production.
// ---------------------------------------------------------------------
builder.Configuration.AddJsonFile(ConfigPath(), optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables(prefix: "ERP_AGENT_");

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<CatalogueUploadOptions>(
    builder.Configuration.GetSection(CatalogueUploadOptions.SectionName));

// ---------------------------------------------------------------------
// Logging — Serilog file sink + console, see ADR-0024 §2. App code stays
// on ILogger<T>; Serilog is only referenced here at the boundary.
// ---------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Services.AddSerilog((sp, lc) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    lc.ReadFrom.Configuration(cfg)
      .Enrich.FromLogContext()
      .WriteTo.Console()
      .WriteTo.File(
          path: Path.Combine(LogsDirectory(), "agent-.log"),
          rollingInterval: RollingInterval.Day,
          retainedFileCountLimit: 7,
          shared: true);
});

// ---------------------------------------------------------------------
// Domain services. The HttpClient's BaseAddress comes from
// AgentOptions.ApiBaseUrl after the configuration system has bound; we
// configure it here so HttpClientFactory hands out clients with the right
// base for every request.
// ---------------------------------------------------------------------
builder.Services.AddSingleton<SaveFolderResolver>();
builder.Services.AddSingleton<IAgentStatus, AgentStatus>();

builder.Services.AddHttpClient<ISaveUploader, HttpSaveUploader>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.ApiBaseUrl))
    {
        http.BaseAddress = new Uri(opts.ApiBaseUrl, UriKind.Absolute);
    }
    http.Timeout = TimeSpan.FromMinutes(2);
});

// Log-tail shipper (#210). Separate HttpClient so its 30-second timeout
// doesn't fight the save uploader's larger one.
builder.Services.AddHttpClient<ILogTailUploader, HttpLogTailUploader>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.ApiBaseUrl))
    {
        http.BaseAddress = new Uri(opts.ApiBaseUrl, UriKind.Absolute);
    }
    http.Timeout = TimeSpan.FromSeconds(30);
});

// Catalogue uploader — separate HttpClient with a longer timeout since
// Docs.json can run 30+ MB and the LXC's upload bandwidth isn't huge.
builder.Services.AddHttpClient<ICatalogueUploader, HttpCatalogueUploader>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(opts.ApiBaseUrl))
    {
        http.BaseAddress = new Uri(opts.ApiBaseUrl, UriKind.Absolute);
    }
    http.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddHostedService<SaveFolderWatcher>();
builder.Services.AddHostedService<LogTailBackgroundService>();
builder.Services.AddHostedService<CatalogueUploadStartup>();

var host = builder.Build();

// Surface unconfigured ApiBaseUrl early — the watcher will still boot in
// degraded mode if the save folder is missing, but uploads can't go
// anywhere without a target.
var options = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
{
    var log = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    log.LogWarning(
        "Agent:ApiBaseUrl is not configured. Set it via {ConfigPath} or ERP_AGENT_ApiBaseUrl. "
        + "Uploads will fail until this is set.",
        ConfigPath());
}
if (string.IsNullOrWhiteSpace(options.AgentToken))
{
    var log = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    log.LogWarning(
        "Agent:AgentToken is empty. The v1 server rejects empty tokens with 401 "
        + "(auth seam per ADR-0024 §5). Set any non-empty string.");
}

await host.RunAsync().ConfigureAwait(false);
return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        erp-agent — ERP for Factory Games local-data agent.

        Usage:
          erp-agent                       Run in the foreground (or as a registered
                                          service when launched by the SCM / systemd).
          erp-agent --install             Register as a Windows service (run elevated)
                                          or a systemd --user unit. Also registers the
                                          erp-agent:// URL protocol handler for the
                                          deep-link pairing flow.
          erp-agent --uninstall           Reverse of --install.
          erp-agent --setup               Interactive first-run wizard: prompts for
            [--token X]                     API URL, agent token, optional save-folder
            [--api Y]                       override. Validates the token against
            [--save-folder Z]               /api/me and writes agent.json.
          erp-agent --pair erp-agent://pair?token=...&api=...
                                          Non-interactive pairing: parse the deep-link,
                                          validate the token, write agent.json. Same
                                          path used by the URL protocol handler.
          erp-agent erp-agent://...       Equivalent to --pair (positional invocation).
          erp-agent --version, -v         Print the agent version.
          erp-agent --help, -h            This help.

        Configuration:
          appsettings.json next to the binary holds defaults.
          agent.json under %ProgramData%/ErpForFactoryGames/ (Windows) or
          $XDG_CONFIG_HOME/ErpForFactoryGames/ (Linux) is the writeable
          override — that's where the API URL + token live. On Windows
          the path is machine-wide rather than per-user so the LocalSystem
          service and the installing user agree on which file to read.

        See INSTALL.md next to the binary for the full first-run guide.
        """);
}

static async Task<int> RunPairAsync(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.Error.WriteLine("--pair requires a erp-agent:// URL argument.");
        return 2;
    }

    var parsed = PairingUrlParser.TryParse(url);
    if (!parsed.IsSuccess)
    {
        Console.Error.WriteLine($"Could not parse pairing URL: {parsed.ErrorMessage}");
        return 2;
    }

    var pairing = BuildPairingService(parsed.Payload.ApiBaseUrl);
    Console.Out.WriteLine($"Pairing against {parsed.Payload.ApiBaseUrl}…");
    var result = await pairing.PairAsync(parsed.Payload.ApiBaseUrl, parsed.Payload.Token).ConfigureAwait(false);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"Pairing failed: {result.ErrorMessage}");
        return 4;
    }

    var paired = result.Paired!;
    Console.Out.WriteLine($"Paired as '{paired.DisplayName}' ({paired.PlayerId}).");
    Console.Out.WriteLine($"Wrote config: {paired.ConfigPath}");
    Console.Out.WriteLine("Restart the agent service to pick up the new values:");
    Console.Out.WriteLine(OperatingSystem.IsWindows()
        ? "  sc.exe stop erp-agent && sc.exe start erp-agent"
        : "  systemctl --user restart erp-agent");
    return 0;
}

static async Task<int> RunSetupAsync(string[] argv)
{
    string? token = null;
    string? api = null;
    string? saveFolder = null;
    var interactive = true;

    for (var i = 0; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--token" when i + 1 < argv.Length:
                token = argv[++i];
                interactive = false;
                break;
            case "--api" when i + 1 < argv.Length:
                api = argv[++i];
                break;
            case "--save-folder" when i + 1 < argv.Length:
                saveFolder = argv[++i];
                break;
        }
    }

    // Tentative API for building the HttpClient; the SetupService will
    // re-prompt if it's null, so we use a placeholder that will be
    // overwritten before any network call. The client just needs a
    // BaseAddress at construction; PairingService passes the real URL.
    var setup = new SetupService(BuildPairingService(api ?? "https://localhost"));
    return await setup
        .RunAsync(new SetupArgs(api, token, saveFolder, interactive))
        .ConfigureAwait(false);
}

static PairingService BuildPairingService(string apiBaseUrlHint)
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    if (Uri.TryCreate(apiBaseUrlHint, UriKind.Absolute, out var uri))
    {
        http.BaseAddress = uri;
    }
    var writer = new AgentConfigWriter(ConfigPath());
    return new PairingService(writer, http);
}

// Local helpers — file paths are OS-specific. See ADR-0024 §2.
//
// Windows uses %ProgramData% rather than %LocalAppData% so that the
// LocalSystem-mode service and the installing user resolve the same
// path. %LocalAppData% would expand to C:\Windows\System32\config\
// systemprofile\AppData\Local\ for the service, which the user can't
// find or edit. Linux keeps per-user XDG paths because the systemd
// --user unit installed by --install runs as the user already.
static string LogsDirectory()
{
    var baseDir = OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        : Environment.GetEnvironmentVariable("XDG_STATE_HOME")
          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
    var dir = Path.Combine(baseDir, "ErpForFactoryGames", "agent-logs");
    Directory.CreateDirectory(dir);
    return dir;
}

static string ConfigPath()
{
    var baseDir = OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    var dir = Path.Combine(baseDir, "ErpForFactoryGames");
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "agent.json");
}

// Lets ILogger<Program> work — the top-level statements compile into a
// generated `Program` class, but Microsoft.Extensions.Logging needs a
// concrete public type for the generic parameter.
public partial class Program { }
