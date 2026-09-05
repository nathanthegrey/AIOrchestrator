using FakeClaude;

// The fake `claude`. Only PRINT mode is simulated — the interactive TUI, --bg and the daemon are
// not, because nothing the bridge automates goes through them (the Live contract tests cover those
// against the real binary). Exit codes and error texts mirror the real CLI where they were
// measured; where they were not, the fake is STRICTER (it refuses combinations whose real
// behaviour is unknown), so a bridge that passes the fake never relies on an unmeasured shape.

var rawArgs = args.ToList();
var parsed = FakeClaudeArguments.Parse(rawArgs);

if (parsed.Version)
{
    Console.Out.WriteLine($"{FakeClaudeScenario.CLI_VERSION} (Claude Code) [FakeClaude]");
    return 0;
}

if (!parsed.Print)
{
    Console.Error.WriteLine("FakeClaude: only --print (-p) mode is simulated; an interactive session cannot be faked");
    return 2;
}

if (parsed.OutputFormat != null && parsed.OutputFormat != "json" && parsed.OutputFormat != "text")
{
    Console.Error.WriteLine($"Error: --output-format must be one of text, json, stream-json (got '{parsed.OutputFormat}')");
    return 1;
}

if (parsed.SessionId != null && !Guid.TryParse(parsed.SessionId, out _))
{
    Console.Error.WriteLine("Error: --session-id must be a valid UUID");
    return 1;
}

if (parsed.SessionId != null && parsed.Resume != null)
{
    // Unmeasured on the real CLI. Refused here so the bridge never sends both.
    Console.Error.WriteLine("FakeClaude: --session-id and --resume together is not a measured combination — refusing");
    return 1;
}

if (parsed.Unknown.Count > 0)
{
    Console.Error.WriteLine($"error: unknown option '{parsed.Unknown[0]}'");
    return 1;
}

var prompt = parsed.PositionalPrompt ?? Read_StdinPrompt();

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Error: Input must be provided either through stdin or as a prompt argument when using --print");
    return 1;
}

var workingDirectory = Directory.GetCurrentDirectory();
var scenario = FakeClaudeScenario.Load(workingDirectory);
var logPath = Invocation_Logger.Resolve_LogPath(workingDirectory);
var (overall, forName) = Invocation_Logger.Append(logPath, rawArgs, parsed, prompt, workingDirectory);
var turn = scenario.Resolve_Turn(parsed.Name, parsed.Name != null && scenario.TurnsByName.ContainsKey(parsed.Name) ? forName : overall);

var sessionId = parsed.SessionId ?? parsed.Resume ?? Guid.NewGuid().ToString();
var model = Resolve_ModelId(parsed.Model);

Simulate_Hooks(scenario, turn, workingDirectory, parsed.Resume != null);

if (turn.DelayMilliseconds > 0)
    Thread.Sleep(turn.DelayMilliseconds);

if (turn.Stderr.Length > 0)
    Console.Error.WriteLine(turn.Stderr);

if (turn.StdoutPrefix.Length > 0)
    Console.Out.Write(turn.StdoutPrefix);

if (parsed.OutputFormat == "json")
    Console.Out.WriteLine(ResultJson_Builder.Build(turn, sessionId, model, numTurns: 2));
else
    Console.Out.WriteLine(turn.Result);

return turn.Resolve_ExitCode();

static string Read_StdinPrompt()
{
    if (Console.IsInputRedirected)
        return Console.In.ReadToEnd().Trim();

    // The real CLI waits 3 s for stdin and says so; the fake says so and does not wait.
    Console.Error.WriteLine("Warning: no stdin data received in 3s, proceeding without it (FakeClaude: stdin is a terminal)");
    return string.Empty;
}

static string Resolve_ModelId(string? model)
{
    return (model ?? "haiku").ToLowerInvariant() switch
    {
        "haiku" => "claude-haiku-4-5-20251001",
        "sonnet" => "claude-sonnet-4-5-20250929",
        "opus" => "claude-opus-4-1-20250805",
        var other => other,
    };
}

/// <summary>Appends one line per hook event, in the format the probe's hook.sh writes (MEASUREMENTS.md §M1).</summary>
static void Simulate_Hooks(FakeClaudeScenario scenario, FakeClaudeTurn turn, string workingDirectory, bool isResume)
{
    if (scenario.HooksLogFile == null)
        return;

    var path = Path.IsPathRooted(scenario.HooksLogFile) ? scenario.HooksLogFile : Path.Combine(workingDirectory, scenario.HooksLogFile);
    var stamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    var lines = turn.Hooks.Select(hook =>
        $"=== {hook} {stamp} pid={Environment.ProcessId} ppid=0 tty=not a tty{(hook == "SessionStart" ? $" source={(isResume ? "resume" : "startup")}" : string.Empty)}\n");

    File.AppendAllText(path, string.Concat(lines));
}
