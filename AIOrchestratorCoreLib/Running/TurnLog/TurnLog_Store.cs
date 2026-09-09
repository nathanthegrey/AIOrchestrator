using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnLog;

/// <summary>
/// WHAT THE OWNER USED TO TAKE A SCREENSHOT OF. A terminal session shows its work in a window; a
/// bridge-driven one has no window, and until now the only durable trace of a turn was the entry it
/// finally wrote — so "what is it doing right now" had no answer and "why did that turn fail" had a
/// 600-character tail of stderr. This file is the trace: one JSON line per stream event, or one per
/// print turn, beside the session's own state file.
///
/// <para>
/// IT IS A RING, NOT AN ARCHIVE. A supervisor left running for a day writes a lot of events, and
/// nothing here is worth unbounded disk: past <see cref="MAX_BYTES"/> the file is rolled to a single
/// <c>.1</c> sibling and started again, so at most two files exist and the reader spans both. The
/// channel remains the record; this is the tail.
/// </para>
/// <para>
/// A FAILURE TO WRITE IT IS NEVER A FAILURE OF THE TURN. Every method here swallows its I/O errors:
/// losing the ability to answer /tail is not a reason to lose the work.
/// </para>
/// </summary>
public static class TurnLog_Store
{
    public const string FILE_NAME = "turns.jsonl";
    public const string ROLLED_SUFFIX = ".1";
    public const long MAX_BYTES = 4 * 1024 * 1024;

    public const string KIND_KEY = "aiorch_kind";
    public const string KIND_TURN = "turn";
    public const string KIND_EVENT = "event";

    /// <summary>
    /// A CLOSING TURN, not a turn: the one short print turn that resumes a transcript the deadline
    /// killed and asks it where it got to. It carries the killed turn's request id with
    /// <c>-closing</c> on the end, so /tail shows the pair together and nothing has to compare costs
    /// between two records that look like two attempts at the same work.
    /// </summary>
    public const string KIND_CLOSING_TURN = "closing";
    public const string REQUEST_ID_KEY = "aiorch_request_id";
    public const string AT_KEY = "aiorch_at";

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId);
        var folder = Path.GetDirectoryName(stateFile) ?? paths.Root;

        // The supervisor's and communicator's state files share the orchestration folder under a
        // role prefix, so their logs must too, or two roles would write one file.
        var prefix = Path.GetFileName(stateFile).Replace(PrintSessionState_Store.STATE_FILE_NAME, string.Empty);

        return Path.Combine(folder, prefix + FILE_NAME);
    }

    /// <summary>One line of the raw stream, with the request it belongs to stamped on it.</summary>
    public static void Append_StreamEvent(string logFile, string requestId, string rawLine)
    {
        var json = StreamTurn.StreamEvent_Reader.Parse_OrNull(rawLine);

        // A line that is not JSON is still evidence — kept as a string so the file stays readable
        // line by line and the reader never has to guess whether a line parses.
        var record = json ?? new JsonObject { ["type"] = "unparsed", ["raw"] = rawLine };

        Append(logFile, Stamp(record, KIND_EVENT, requestId));
    }

    /// <summary>One whole print turn: its result document, or what came back instead of one.</summary>
    public static void Append_TurnResult(string logFile, string requestId, ITurnResult result)
    {
        Append_TurnResult(logFile, requestId, result, KIND_TURN);
    }

    /// <summary>The closing turn's result, tagged <see cref="KIND_CLOSING_TURN"/> — same record, different word.</summary>
    public static void Append_ClosingTurnResult(string logFile, string requestId, ITurnResult result)
    {
        Append_TurnResult(logFile, requestId, result, KIND_CLOSING_TURN);
    }

    static void Append_TurnResult(string logFile, string requestId, ITurnResult result, string kind)
    {
        var record = StreamTurn.StreamEvent_Reader.Parse_OrNull(result.RawStdout) ?? new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = result.Subtype,
            ["is_error"] = result.IsError,
            ["result"] = result.ResultText,
            ["session_id"] = result.SessionId,
            ["total_cost_usd"] = result.TotalCostUsd,
            ["duration_ms"] = result.DurationMs,
            ["exit_code"] = result.ExitCode,
            ["stderr_tail"] = Tail(result.RawStderr, 2000),
        };

        Append(logFile, Stamp(record, kind, requestId));
    }

    /// <summary>
    /// The last <paramref name="count"/> lines, oldest first, spanning the rolled sibling when the
    /// live file alone does not hold enough. Empty for a session that has not run yet — which is an
    /// answer, not an error.
    /// </summary>
    public static IReadOnlyList<JsonObject> Read_LastRecords(string logFile, int count)
    {
        List<string> lines = [.. Read_Lines(logFile + ROLLED_SUFFIX), .. Read_Lines(logFile)];
        List<JsonObject> records = [];

        foreach (var line in lines.Skip(Math.Max(0, lines.Count - count)))
        {
            if (StreamTurn.StreamEvent_Reader.Parse_OrNull(line) is JsonObject record)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Every record of the LAST turn — from the first record carrying the newest request id. Keyed
    /// on the request id rather than on a result boundary because a straggler event (the <c>Stop</c>
    /// hook answers after the result, measured) would otherwise open a phantom turn of its own.
    /// </summary>
    public static IReadOnlyList<JsonObject> Read_LastTurn(string logFile, int maxRecords)
    {
        var records = Read_LastRecords(logFile, maxRecords);
        var newest = records.LastOrDefault(record => Read_RequestId_OrNull(record) != null);

        if (newest == null)
            return records;

        var requestId = Read_RequestId_OrNull(newest);

        return [.. records.Where(record => Read_RequestId_OrNull(record) == requestId)];
    }

    public static string? Read_RequestId_OrNull(JsonObject record)
    {
        try
        {
            return record[REQUEST_ID_KEY]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public static DateTime? Read_At_OrNull(JsonObject record)
    {
        try
        {
            return DateTime.TryParse(record[AT_KEY]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : null;
        }
        catch
        {
            return null;
        }
    }

    static JsonObject Stamp(JsonObject record, string kind, string requestId)
    {
        var stamped = record.DeepClone().AsObject();

        stamped[KIND_KEY] = kind;
        stamped[REQUEST_ID_KEY] = requestId;
        stamped[AT_KEY] = DateTime.UtcNow.ToString("o");

        return stamped;
    }

    static void Append(string logFile, JsonObject record)
    {
        try
        {
            var folder = Path.GetDirectoryName(logFile);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            Roll_IfTooBig(logFile);
            File.AppendAllText(logFile, record.ToJsonString() + "\n");
        }
        catch
        {
            // See the class remark: the tail is never worth the turn.
        }
    }

    static void Roll_IfTooBig(string logFile)
    {
        try
        {
            if (!File.Exists(logFile) || new FileInfo(logFile).Length < MAX_BYTES)
                return;

            File.Move(logFile, logFile + ROLLED_SUFFIX, overwrite: true);
        }
        catch
        {
            // A file that cannot be rolled keeps growing; that is better than losing the turn.
        }
    }

    static IReadOnlyList<string> Read_Lines(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch
        {
            return [];
        }
    }

    static string Tail(string text, int maxLength)
    {
        var trimmed = (text ?? string.Empty).Trim();

        return trimmed.Length <= maxLength ? trimmed : "…" + trimmed[^maxLength..];
    }
}
