using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Running.TurnResult;

/// <summary>
/// Reads the result document out of a print invocation's stdout. The document is the LAST line
/// that parses as a JSON object — the CLI may print warnings before it ("no stdin data received")
/// and a hook failure lands on stderr, not here. No document at all is still a result: the text
/// becomes the result text (text mode), and the exit code says whether it worked.
/// </summary>
public static class TurnResult_Parser
{
    public static ITurnResult Parse(int exitCode, bool timedOut, string stdout, string stderr, TimeSpan elapsed)
    {
        var document = Find_ResultDocument_OrNull(stdout);

        if (document == null)
        {
            var text = stdout.Trim();

            return TurnResult_Factory.Create(exitCode, timedOut, exitCode != 0 || timedOut, null, text.Length > 0 ? text : null, null, null, null, null, null, null, stdout, stderr, elapsed);
        }

        return TurnResult_Factory.Create(
            exitCode,
            timedOut,
            Read_Bool(document, "is_error") ?? exitCode != 0,
            Read_String(document, "subtype"),
            Read_String(document, "result"),
            Read_String(document, "session_id"),
            Read_Double(document, "total_cost_usd"),
            Read_Long(document, "duration_ms"),
            Read_Long(document, "duration_api_ms"),
            (int?)Read_Long(document, "num_turns"),
            (int?)Read_Long(document, "api_error_status"),
            stdout,
            stderr,
            elapsed);
    }

    static JsonObject? Find_ResultDocument_OrNull(string stdout)
    {
        var lines = stdout.Split('\n');

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();

            if (!line.StartsWith('{'))
                continue;

            try
            {
                if (JsonNode.Parse(line) is JsonObject document)
                    return document;
            }
            catch
            {
                // Not the document — a brace-led line of prose. Keep looking upward.
            }
        }

        return null;
    }

    static string? Read_String(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<string?>();
        }
        catch
        {
            return null;
        }
    }

    static bool? Read_Bool(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<bool?>();
        }
        catch
        {
            return null;
        }
    }

    static double? Read_Double(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<double?>();
        }
        catch
        {
            return null;
        }
    }

    static long? Read_Long(JsonObject document, string key)
    {
        try
        {
            var node = document[key];

            if (node == null)
                return null;

            return (long)node.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }
}
