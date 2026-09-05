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

    /// <summary>
    /// Whether a process has already run under this <c>--session-id</c> — read from the log, which
    /// is this fake's whole memory. The real CLI refuses a re-claimed id (measured); so does this.
    /// </summary>
    public static bool Has_ClaimedSessionId(string logPath, string sessionId)
    {
        if (!File.Exists(logPath))
            return false;

        foreach (var line in File.ReadAllLines(logPath))
        {
            if (line.Length == 0)
                continue;

            try
            {
                if ((JsonNode.Parse(line) as JsonObject)?["session_id"]?.GetValue<string>() == sessionId)
                    return true;
            }
            catch
            {
                // A half-written line is not a claim.
            }
        }

        return false;
    }

    public static string Resolve_LogPath(string workingDirectory)
    {
        var envPath = Environment.GetEnvironmentVariable(FakeClaudeScenario.LOG_ENV);

        return !string.IsNullOrWhiteSpace(envPath) ? envPath : Path.Combine(workingDirectory, FakeClaudeScenario.LOG_FILE);
    }

    /// <summary>Appends this invocation and returns its number, overall and among invocations with the same name.</summary>
    public static (int Overall, int ForName) Append(string logPath, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory)
    {
        // SHARED, AND CONCURRENTLY. The dispatcher allows ten turns at once and they share one
        // working directory, so two fakes write this file at the same moment — and
        // File.AppendAllText opens it FileShare.Read, which makes the second one die with an
        // IOException the suite would read as a turn error. Read and append under one exclusive
        // handle, retried, so the counters below are computed from what is really there.
        return Append_Serialised(logPath, rawArgs, args, prompt, workingDirectory);
    }

    static (int Overall, int ForName) Append_Serialised(string logPath, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory)
    {
        var folder = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return Append_ToOpenLog(stream, rawArgs, args, prompt, workingDirectory);
            }
            catch (IOException) when (attempt < 200)
            {
                Thread.Sleep(25);
            }
        }
    }

    static (int Overall, int ForName) Append_ToOpenLog(FileStream stream, IReadOnlyList<string> rawArgs, FakeClaudeArguments args, string prompt, string workingDirectory)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var existing = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
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

        stream.Seek(0, SeekOrigin.End);

        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(record.ToJsonString() + "\n");
        writer.Flush();

        return (overall, forName);
    }
}
