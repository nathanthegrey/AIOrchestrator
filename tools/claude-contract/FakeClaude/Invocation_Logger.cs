using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// Records every invocation as one JSON line — the flags, the prompt, the working directory, the
/// AIORCH_* environment — so a test can assert what the bridge actually sent, not what it meant
/// to. The line count is also the invocation counter the scenario's turn list is indexed by.
/// Resolved from <c>FAKE_CLAUDE_LOG</c>, else <c>fake-claude-invocations.jsonl</c> in the working
/// directory.
/// </summary>
public static class Invocation_Logger
{
    static readonly string[] LOGGED_ENVIRONMENT_PREFIXES = ["AIORCH_", "CLAUDECODE", "CLAUDE_CODE_"];

    public static string Resolve_LogPath(string workingDirectory)
    {
        var envPath = Environment.GetEnvironmentVariable(FakeClaudeScenario.LOG_ENV);

        return !string.IsNullOrWhiteSpace(envPath) ? envPath : Path.Combine(workingDirectory, FakeClaudeScenario.LOG_FILE);
    }

    /// <summary>Appends this invocation and returns its number, overall and among invocations with the same name.</summary>
    public static (int Overall, int ForName) Append(string logPath, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory)
    {
        var existing = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        var overall = existing.Length + 1;
        var forName = 1;

        foreach (var line in existing)
        {
            if (line.Length == 0)
                continue;

            var previousName = (JsonNode.Parse(line) as JsonObject)?["name"]?.GetValue<string>();

            if (previousName == args.Name)
                forName++;
        }

        var environment = new JsonObject();

        foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
        {
            var key = pair.Key.ToString() ?? string.Empty;

            if (LOGGED_ENVIRONMENT_PREFIXES.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                environment[key] = pair.Value?.ToString();
        }

        var arguments = new JsonArray();

        foreach (var raw in rawArgs)
            arguments.Add(raw);

        var record = new JsonObject
        {
            ["n"] = overall,
            ["n_for_name"] = forName,
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["pid"] = Environment.ProcessId,
            ["name"] = args.Name,
            ["args"] = arguments,
            ["prompt"] = prompt,
            ["prompt_source"] = args.PositionalPrompt != null ? "argument" : "stdin",
            ["cwd"] = workingDirectory,
            ["resume"] = args.Resume,
            ["session_id"] = args.SessionId,
            ["settings"] = args.Settings,
            ["model"] = args.Model,
            ["permission_mode"] = args.PermissionMode,
            ["env"] = environment,
        };

        var folder = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        File.AppendAllText(logPath, record.ToJsonString() + "\n");

        return (overall, forName);
    }
}
