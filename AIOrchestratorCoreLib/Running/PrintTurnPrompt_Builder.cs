using System.Text;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// The stdin prompt of a resumed turn. It names the request, lists the entries that triggered it
/// (verbatim — one tool call saved, and the transcript then holds them), restates the one rule the
/// role command's print paragraph states (the final message IS the entry), and — after a bridge
/// restart only — opens with the turns already executed, so a session whose transcript remembers
/// answering does not answer twice (CLAUDE.md decision 8, defended where the effect is).
/// </summary>
public static class PrintTurnPrompt_Builder
{
    public const string ALREADY_EXECUTED_PREFIX = "Turns already executed by the bridge for this session:";

    public static string Build_FollowUp(string requestId, IReadOnlyList<IChannelEntry> pending, IReadOnlyList<int> alreadyExecutedTurns)
    {
        var prompt = new StringBuilder();

        prompt.Append("[bridge turn ").Append(requestId).Append("]\n");

        if (alreadyExecutedTurns.Count > 0)
        {
            prompt.Append(ALREADY_EXECUTED_PREFIX).Append(' ')
                .Append(string.Join(", ", alreadyExecutedTurns))
                .Append(" — their work is done and their entries are in your channel; do not repeat it. This turn is a resume after a bridge restart.\n");
        }

        prompt.Append("New traffic in your channel — ").Append(pending.Count).Append(pending.Count == 1 ? " entry" : " entries").Append(":\n\n");

        foreach (var entry in pending)
            prompt.Append(entry.RawText.Trim()).Append("\n\n");

        prompt.Append("Act on it per your role command. Your final message IS your channel entry — the bridge appends it under your author word with the header, the index and the time: first line the subject, then a blank line, then the body. A question ends the turn exactly as an answer does.\n");

        return prompt.ToString();
    }
}
