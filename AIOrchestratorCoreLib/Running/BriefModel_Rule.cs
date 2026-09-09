using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// THE MODEL A BRIEF ASKS FOR. The supervisor writes <c>MODEL: sonnet</c> in the brief and the
/// member's next turn runs on it, instead of riding the role default it was registered with.
///
/// <para>
/// WHY IT IS POSSIBLE AT ALL, and why only now (owner decision 2026-09-09): the model used to be
/// chosen when a member was created and then frozen for its life, because a member was a living
/// process and you cannot change the model of a process that is already running. Every member turn
/// is now a FRESH process, so the choice costs nothing to move — and a fixed model per role made no
/// sense to the owner (2026-09-07, the same reason <c>add-implementer</c> already carries a
/// <c>model</c>): a one-line fix and a redesign are not the same job, and the supervisor is the one
/// session that knows which of the two it is about to hand over.
/// </para>
/// <para>
/// THE FORMAT IS ONE LINE, in the same shape as the <c>QUESTION:</c> / <c>OPTION:</c> /
/// <c>DEADLINE:</c> / <c>TO:</c> markers the entries already use — the marker, a colon, one word.
/// A model asked to write prose gets that right without being told twice, and every failure of it
/// stays legible in the channel file afterwards.
/// </para>
/// <para>
/// EVERY <c>MODEL:</c> LINE ANSWERS SOMETHING — a model or a NAMED REFUSAL, never silence. Unlike
/// <see cref="TurnReply_Splitter"/>, whose <c>TO:</c> has to stay tolerant because "to: whoever
/// picks this up" is ordinary prose, a line that begins with this marker is not a sentence anyone
/// writes by accident. So an argument that is empty, more than one word, or a word this app does not
/// run is REFUSED WITH A REASON the caller logs, rather than passed over: the turn still runs, on
/// the model the session was registered with, but decision 21's rule holds — a predicate that could
/// not be evaluated says which one and why, and never grants silent consent.
/// </para>
/// <para>
/// <c>fable</c> IS REFUSED, and that is not this rule's opinion: it is the owner's standing rule
/// that fable is theirs to choose. The app enforces the half it can (decision 21), exactly as
/// <see cref="OrchestrationRequests_Reader.FORBIDDEN_MEMBER_MODEL"/> already refuses it on the
/// request file — the same constant, so the two can never come to disagree about the spelling.
/// </para>
/// <para>
/// THE LAST LINE WINS, and it wins even when it is the WORSE of the two. A supervisor that writes
/// <c>MODEL: sonnet</c> and then, further down, <c>MODEL: opus</c> has changed its mind in writing
/// order, and a rule that preferred the first would silently obey a sentence its author had already
/// corrected. The same holds in the other direction — a last line naming <c>fable</c> or a typo is a
/// refusal, not an invitation to fall back to an accepted line above it, because "the supervisor's
/// latest word was unusable" and "the supervisor asked for sonnet" are different facts and only the
/// first is true.
/// </para>
/// </summary>
public static class BriefModel_Rule
{
    public const string MODEL_MARKER = "MODEL:";

    /// <summary>
    /// The words the CLI's <c>--model</c> takes and this app already writes into config.json — the
    /// aliases, never a full model id: an id pins a snapshot that expires, and none of the app's own
    /// defaults (<c>OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL</c> and its neighbours) is
    /// spelled any other way.
    /// </summary>
    public static readonly IReadOnlyList<string> ACCEPTED_MODELS = ["opus", "sonnet", "haiku"];

    /// <summary>Opens and closes a markdown code fence. Text between two of them is quoted, never an instruction.</summary>
    const string FENCE = "```";

    /// <summary>"opus, sonnet or haiku" — the accepted words in a refusal a person has to act on.</summary>
    public static string Describe_Accepted()
    {
        var words = ACCEPTED_MODELS;

        return words.Count < 2
            ? string.Join(", ", words)
            : $"{string.Join(", ", words.Take(words.Count - 1))} or {words[^1]}";
    }

    /// <summary>
    /// THE MODEL THIS TURN RUNS ON: what the pending briefs asked for, or
    /// <paramref name="registeredModel"/> when they asked for nothing usable. The dispatcher's whole
    /// question, in one call — <paramref name="pendingEntries"/> is the traffic the turn is about to
    /// be handed, in the order it will be handed, and <paramref name="registeredModel"/> is
    /// <c>IPrintSessionState.Model</c>.
    ///
    /// <para>
    /// <c>RefusalReason</c> is non-null only when a <c>MODEL:</c> line was found and could not be
    /// honoured; it is the caller's to LOG (the orchestration log, per decision 21 — not Telegram,
    /// per decision 15: the owner cannot act on a supervisor's typo). A null reason with a
    /// <c>Model</c> equal to <paramref name="registeredModel"/> is the ordinary case: nobody asked.
    /// </para>
    /// </summary>
    public static (string? Model, string? RefusalReason) Resolve_ForTurn(IEnumerable<IChannelEntry> pendingEntries, string? registeredModel)
    {
        var asked = Resolve_FromEntries(pendingEntries);

        return (asked.Model ?? registeredModel, asked.RefusalReason);
    }

    /// <summary>
    /// The last thing the supervisor said about the model across a batch of entries, or nulls when
    /// it said nothing. Separate from <see cref="Resolve_ForTurn"/> because "what was asked for" and
    /// "what the turn therefore runs on" are two questions, and only the first is about the channel.
    /// </summary>
    public static (string? Model, string? RefusalReason) Resolve_FromEntries(IEnumerable<IChannelEntry> entries)
    {
        (string? Model, string? RefusalReason) latest = (null, null);

        foreach (var entry in entries)
        {
            var asked = Resolve_FromEntry(entry);

            // A batch whose LAST marker is in an earlier entry keeps that entry's answer: entries
            // arrive in delivery order, so the last one to carry a marker is the supervisor's latest
            // word, and an entry that carries none says nothing about the model either way.
            if (asked.Model != null || asked.RefusalReason != null)
                latest = asked;
        }

        return latest;
    }

    /// <summary>
    /// One entry's answer. Nulls in both fields mean the entry carries no <c>MODEL:</c> line at all —
    /// which is nearly every entry ever written.
    ///
    /// <para>
    /// ONLY A SUPERVISOR MAY SET IT. A member choosing its own model is the conflict of interest the
    /// request reader already refuses (<see cref="OrchestrationRequests_Reader.FORBIDDEN_MEMBER_MODEL_MESSAGE"/>):
    /// the session that would benefit from the expensive model is not the session that pays for it.
    /// The OWNER is excluded too, and for the opposite reason — they have <c>set-model</c>, which
    /// persists a per-orchestration override in session.json, and a rule that also read their prose
    /// would give one decision two mechanisms that outlive each other differently.
    /// </para>
    /// </summary>
    public static (string? Model, string? RefusalReason) Resolve_FromEntry(IChannelEntry entry)
    {
        if (entry.Author != ChannelAuthors.Supervisor)
            return (null, null);

        var argument = Read_LastArgument_OrNull(entry.Subject, entry.Body);

        if (argument == null)
            return (null, null);

        return Classify(argument, entry.Index);
    }

    /// <summary>
    /// The argument of the LAST <c>MODEL:</c> line, or null when there is none. The subject is read
    /// as the first line and the body follows, which is the order the supervisor wrote them in — so a
    /// marker in the body supersedes one in the subject, and not the other way round.
    /// </summary>
    static string? Read_LastArgument_OrNull(string subject, string body)
    {
        string? argument = null;

        // THE SUBJECT IS NEVER INSIDE A FENCE. It is one line of a header the parser has already
        // isolated, so a fence opened in a previous entry's body cannot reach it.
        if (Read_MarkerArgument_OrNull(subject) is string fromSubject)
            argument = fromSubject;

        var insideFence = false;

        foreach (var line in (body ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            // A MARKER INSIDE A FENCE IS QUOTED TEXT, the lesson TurnReply_Splitter already learned
            // for its own marker: quoting the protocol back is ordinary model behaviour, and this
            // marker is IN the brief the supervisor was just handed. A supervisor pasting a member's
            // report — or this rule's own documentation — into a fence must not thereby move the
            // member onto another model.
            if (line.TrimStart().StartsWith(FENCE, StringComparison.Ordinal))
            {
                insideFence = !insideFence;
                continue;
            }

            if (insideFence)
                continue;

            if (Read_MarkerArgument_OrNull(line) is string fromBody)
                argument = fromBody;
        }

        return argument;
    }

    /// <summary>
    /// What a line puts after the marker — <c>string.Empty</c> for a bare <c>MODEL:</c>, null when
    /// the line is not a marker line at all. Empty and null are deliberately different: the first is
    /// a supervisor who tried and gets told, the second is prose that never mentioned the subject.
    /// </summary>
    static string? Read_MarkerArgument_OrNull(string? line)
    {
        var trimmed = (line ?? string.Empty).Trim();

        // THE MARKER STARTS THE LINE. Case-insensitive because it is written by hand into prose, but
        // anchored at the start because "the model: whatever you like" is a sentence — the same
        // reading TurnReply_Splitter takes of its own marker, and the reason the accepted shape is
        // one line and one word rather than a phrase found anywhere.
        return trimmed.StartsWith(MODEL_MARKER, StringComparison.OrdinalIgnoreCase)
            ? trimmed[MODEL_MARKER.Length..].Trim()
            : null;
    }

    /// <summary>
    /// The argument, judged. Every branch but the first names its reason in the owner's and the
    /// supervisor's own terms, because a log line saying "model refused" is the silence decision 21
    /// forbids.
    /// </summary>
    static (string? Model, string? RefusalReason) Classify(string argument, int entryIndex)
    {
        // Markdown decoration around the word is stripped for the reason the entry parser strips it
        // from an author and the reply splitter from an address: `MODEL: **sonnet**` is a supervisor
        // formatting its brief, not asking for a model called "**sonnet**".
        var word = argument.Trim('*', '_', '`', '.', ',', ';', ':', '"', '\'');

        if (word.Length == 0)
            return (null, $"entry [{entryIndex}] has a {MODEL_MARKER} line that names no model — {Describe_Accepted()}; this turn runs on the model the session was registered with");

        if (word.Any(char.IsWhiteSpace))
            return (null, $"entry [{entryIndex}] has a {MODEL_MARKER} line naming more than one word ('{Trim_ForLog(word)}') — a model is one word, {Describe_Accepted()}; this turn runs on the model the session was registered with");

        if (string.Equals(word, OrchestrationRequests_Reader.FORBIDDEN_MEMBER_MODEL, StringComparison.OrdinalIgnoreCase))
            return (null, $"entry [{entryIndex}] asks for {MODEL_MARKER} {word} — that one is the owner's to choose and never a session's; this turn runs on the model the session was registered with");

        foreach (var accepted in ACCEPTED_MODELS)
        {
            // The ACCEPTED spelling is returned, not the one that was written: `MODEL: Sonnet`
            // becomes "sonnet", so what reaches --model is a word the CLI takes and what is logged
            // and stored is one spelling of it rather than the supervisor's capitalisation.
            if (string.Equals(word, accepted, StringComparison.OrdinalIgnoreCase))
                return (accepted, null);
        }

        return (null, $"entry [{entryIndex}] asks for {MODEL_MARKER} {Trim_ForLog(word)}, which is not a model this app runs ({Describe_Accepted()}) — this turn runs on the model the session was registered with");
    }

    /// <summary>A refusal quotes the bad value back, and an entry can hold a paragraph after the marker.</summary>
    static string Trim_ForLog(string text)
    {
        var single = text.Replace("\n", " ").Trim();

        return single.Length <= 60 ? single : single[..59] + "…";
    }
}
