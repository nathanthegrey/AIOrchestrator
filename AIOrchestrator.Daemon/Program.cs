using AIOrchestrator.Daemon;
using AIOrchestratorCoreLib.Composition.HostOptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The headless host: the same service graph the WPF app builds, run as a daemon.
//   dotnet run --project AIOrchestrator.Daemon [-- --root DIR] [--claude-home DIR]
// Under systemd the unit's Type=notify and WatchdogSec are honoured (READY=1, WATCHDOG=1);
// under launchd it is a plain KeepAlive process; under the Windows SCM it is a service.
// Ctrl-C and SIGTERM both stop it the way closing the WPF window does: engine cancelled,
// every spawned session killed, instance lock released.

var options = HostOptions_Factory.Create_FromArguments(
    args,
    Environment.GetEnvironmentVariable,
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

var builder = Host.CreateApplicationBuilder(args);

// The orchestrator's own log goes to stdout through ConsoleLog_Writer; the host's framework
// logger is kept for the lifetime's own problems only, so a start-up failure is never silent.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSystemd();
builder.Services.AddWindowsService(serviceOptions => serviceOptions.ServiceName = BridgeHost_Service.SERVICE_NAME);
builder.Services.AddSingleton(options);
builder.Services.AddHostedService<BridgeHost_Service>();

await builder.Build().RunAsync();

return Environment.ExitCode;
