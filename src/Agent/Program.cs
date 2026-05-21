using Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

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
// per-user agent.json (writeable; the install flow drops the token there)
// plus env vars (ERP_AGENT_*). User-secrets only outside Production.
// ---------------------------------------------------------------------
builder.Configuration.AddJsonFile(UserConfigPath(), optional: true, reloadOnChange: true);
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

builder.Services.AddHostedService<SaveFolderWatcher>();

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
        UserConfigPath());
}
if (string.IsNullOrWhiteSpace(options.AgentToken))
{
    var log = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
    log.LogWarning(
        "Agent:AgentToken is empty. The v1 server rejects empty tokens with 401 "
        + "(auth seam per ADR-0024 §5). Set any non-empty string.");
}

await host.RunAsync().ConfigureAwait(false);

// Local helpers — file paths are OS-specific. See ADR-0024 §2.
static string LogsDirectory()
{
    var baseDir = OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        : Environment.GetEnvironmentVariable("XDG_STATE_HOME")
          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
    var dir = Path.Combine(baseDir, "ErpForFactoryGames", "agent-logs");
    Directory.CreateDirectory(dir);
    return dir;
}

static string UserConfigPath()
{
    var baseDir = OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
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
