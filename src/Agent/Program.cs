using Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

// ---------------------------------------------------------------------
// CLI entry — handle --install / --uninstall / --help before any host
// machinery boots. These flags do one thing and exit; the long-running
// service path is the default when no flag is given.
// ---------------------------------------------------------------------
foreach (var arg in args)
{
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

builder.Services.AddHostedService<SaveFolderWatcher>();
builder.Services.AddHostedService<LogTailBackgroundService>();

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
          erp-agent                  Run in the foreground (or as a registered
                                     service when launched by the SCM / systemd).
          erp-agent --install        Register as a Windows service (run elevated)
                                     or a systemd --user unit.
          erp-agent --uninstall      Reverse of --install.
          erp-agent --version, -v    Print the agent version.
          erp-agent --help, -h       This help.

        Configuration:
          appsettings.json next to the binary holds defaults.
          agent.json under %ProgramData%/ErpForFactoryGames/ (Windows) or
          $XDG_CONFIG_HOME/ErpForFactoryGames/ (Linux) is the writeable
          override — that's where you put your API URL + token. On Windows
          the path is machine-wide rather than per-user so the LocalSystem
          service and the installing user agree on which file to read.

        See INSTALL.md next to the binary for the full first-run guide.
        """);
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
