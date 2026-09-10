using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE CHANNEL GRAMMAR, .NET side — the ONE place a marker word exists in C#.
///
/// <para>
/// WHAT IT REPLACES, MEASURED. The same marker was spelled in nine files, and two of the spellings
/// disagreed: <c>OwnerMessage_Contract</c> and <c>OwnerPush_Policy</c> carried <c>"QUESTION:"</c>
/// while <c>OwnerQuestion_Contract</c> carried <c>"QUESTION"</c> — one word, two forms, so the side
/// that WROTE a question and the side that RECOGNISED one could disagree about what a question is.
/// The owner's requirement of 2026-09-10 names this: "writer and recogniser read ONE constant, not
/// one place".
/// </para>
/// <para>
/// WHY THE SOURCE IS A JSON FILE AND NOT THIS CLASS'S OWN CONSTANTS. The writer is
/// <c>kit/bin/channel-append.sh</c> — bash, invoked by a session — and the recognisers are .NET. A
/// constant in either language is unreadable by the other, which is how the drift above happened at
/// all. So the truth is data: <c>kit/grammar/channel-grammar.json</c>, EMBEDDED here as a resource
/// and read by the tool with <c>jq</c> from the kit. <c>ChannelGrammarTests</c> fails if the two
/// copies differ by a byte.
/// </para>
/// <para>
/// IT THROWS RATHER THAN DEFAULTING. A grammar that silently answered "" for a missing marker would
/// make every recogniser match everything — the failure decision 20 is about, one layer down: a
/// harness that cannot find what it needs must refuse, not invent. The resource is embedded at build
/// time, so a throw here is a broken build, never a runtime surprise on the owner's machine.
/// </para>
/// </summary>
public static class ChannelGrammar
{
    /// <summary>
    /// The kit path, so a caller that wants the file on disk (the tool's own copy, the installer)
    /// names it once. Not used to LOAD the grammar — that comes from the embedded resource, which
    /// cannot be missing at runtime.
    /// </summary>
    public const string KIT_RELATIVE_PATH = "kit/grammar/channel-grammar.json";

    const string RESOURCE_NAME = "AIOrchestratorCoreLib.kit.grammar.channel-grammar.json";

    static readonly JsonObject ROOT = Load_Root();

    /// <summary>The raw text of the embedded grammar, for the test that compares it with the kit file.</summary>
    public static string Embedded_Json { get; } = Read_Resource();

    /// <summary>The header regex the parser uses and the tool writes to match.</summary>
    public static string Header_Pattern => Read_String(["header", "pattern"]);

    /// <summary>The header line the TOOL composes: index and stamp are its to compute, never the model's.</summary>
    public static string Header_Template => Read_String(["header", "template"]);

    public static string Header_StampFormat => Read_String(["header", "stamp_format"]);

    public static string Header_Separator => Read_String(["header", "separator"]);

    /// <summary>How the persisted type line opens — requirement 3's typed field.</summary>
    public static string TypeLine_Prefix => Read_String(["type_field", "line_prefix"]);

    public static IReadOnlyList<string> Types => Read_Array(["types", "values"]);

    public static IReadOnlyList<string> RiskLevels => Read_Array(["risk_levels", "values"]);

    public static int Max_Lines => Read_Int(["ceilings", "max_lines"]);

    public static int Max_Characters => Read_Int(["ceilings", "max_characters"]);

    public static int Min_Options => Read_Int(["ceilings", "min_options"]);

    public static int Max_Options => Read_Int(["ceilings", "max_options"]);

    public static int Option_LabelWidth => Read_Int(["ceilings", "option_label_width"]);

    /// <summary>
    /// One marker word, by its grammar key. Named rather than indexed so a typo is a compile error at
    /// the property below and a loud throw here, never a silently empty match.
    /// </summary>
    public static string Marker(string key)
    {
        return Read_String(["markers", key]);
    }

    public static string QUESTION => Marker("question");

    public static string OPTION => Marker("option");

    public static string RECOMMEND => Marker("recommend");

    public static string RISK => Marker("risk");

    public static string ROW => Marker("row");

    public static string DEADLINE => Marker("deadline");

    public static string DEFAULT => Marker("default");

    public static string IMAGE => Marker("image");

    public static string ATTACH => Marker("attach");

    public static string STATE => Marker("state");

    public static string BLOCKED_ON_OWNER => Marker("blocked_on_owner");

    public static string ANSWERED => Marker("answered");

    public static string STANDING_BY => Marker("standing_by");

    public static string WRITING_WINDOW_OPEN => Marker("writing_window_open");

    public static string WRITING_WINDOW_CLOSED => Marker("writing_window_closed");

    public static string MUTATION_WINDOW_OPEN => Marker("mutation_window_open");

    public static string MUTATION_WINDOW_CLOSED => Marker("mutation_window_closed");

    public static string BOOT_ANNOUNCEMENT_WORD => Marker("boot_announcement_word");

    public static string TO => Marker("to");

    public static string WORKTREE => Marker("worktree");

    /// <summary>
    /// A marker word with its trailing colon removed — some recognisers match the bare word because
    /// they read a line the owner may have typed without one. Derived rather than stored, so the two
    /// forms cannot come apart the way `QUESTION:` and `QUESTION` did.
    /// </summary>
    public static string Bare(string marker)
    {
        return marker.TrimEnd(':');
    }

    static string Read_Resource()
    {
        using var stream = typeof(ChannelGrammar).Assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new Exception(
                $"The channel grammar resource '{RESOURCE_NAME}' is not embedded in "
                + $"{typeof(ChannelGrammar).Assembly.GetName().Name}. Every marker word in this app comes from it, so "
                + "there is nothing to fall back to: check the EmbeddedResource entry in AIOrchestratorCoreLib.csproj.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    static JsonObject Load_Root()
    {
        return JsonNode.Parse(Read_Resource()) as JsonObject
            ?? throw new Exception($"The channel grammar ({KIT_RELATIVE_PATH}) is not a JSON object.");
    }

    static JsonNode Read_Node(IReadOnlyList<string> path)
    {
        JsonNode node = ROOT;

        foreach (var step in path)
        {
            node = (node as JsonObject)?[step]
                ?? throw new Exception(
                    $"The channel grammar has no '{string.Join('.', path)}'. It is the single source for every "
                    + "marker word and ceiling in this app, so a missing key is a broken grammar, not a default.");
        }

        return node;
    }

    static string Read_String(IReadOnlyList<string> path)
    {
        var value = Read_Node(path).GetValue<string>();

        if (string.IsNullOrEmpty(value))
            throw new Exception($"The channel grammar's '{string.Join('.', path)}' is empty — an empty marker matches every line.");

        return value;
    }

    static int Read_Int(IReadOnlyList<string> path)
    {
        return Read_Node(path).GetValue<int>();
    }

    static IReadOnlyList<string> Read_Array(IReadOnlyList<string> path)
    {
        var array = Read_Node(path) as JsonArray
            ?? throw new Exception($"The channel grammar's '{string.Join('.', path)}' is not an array.");

        return [.. array.Select(item => item!.GetValue<string>())];
    }
}
