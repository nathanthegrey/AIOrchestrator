using AIOrchestratorCoreLib.Bridge.EngineState;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// WHEN A NEW QUESTION MAY CLOSE AN OLD ONE, AND WHEN IT MAY NOT GO OUT AT ALL.
///
/// <para>
/// Stage 16 (2026-09-10) taught a newer question to close the older ones the owner had already
/// answered in words, and it read "the owner spoke" as "the owner answered". On 2026-09-11 in
/// fincanva-6 that asked the owner the same thing twice: they tapped «Sì» on a high-risk merge
/// question at 10:42 (local), the app edited it to show the read-back code and held the answer, and
/// before typing the code they asked "devo fare solo smoke?". The supervisor, told nothing of the
/// tap, answered and asked the merge question again word for word; the app counted the owner's
/// QUESTION as a reply in words, closed the tapped one as superseded — discarding the pending code —
/// and posted a second copy with live buttons. Three rules below, one per mistake in that chain.
/// </para>
/// </summary>
public static class QuestionSupersede_Decider
{
    /// <summary>
    /// WHETHER AN OWNER MESSAGE COUNTS AS A REPLY for the supersede rule. A message that is itself a
    /// question asks something; it answers nothing, so it cannot make an older question obsolete —
    /// the same reading <see cref="AnswerBinding_Decider"/> gives it for binding.
    /// </summary>
    public static bool Counts_AsAReply(string ownerMessageText)
    {
        return !OwnerPush_Policy.Asks_InProse(ownerMessageText);
    }

    /// <summary>
    /// The open questions of ONE orchestration that a newer question closes: those asked before the
    /// owner last replied in words — EXCEPT a question the owner has already answered by a tap and
    /// whose high-risk read-back is still pending. That question is answered; only the code is
    /// outstanding, and superseding it throws the owner's answer away.
    /// </summary>
    /// <param name="openInOrchestration">The open questions of the orchestration the new one belongs to.</param>
    /// <param name="ownerRepliedUtc">When the owner last replied in words there, or null if never.</param>
    /// <param name="awaitingReadBack">Message ids of questions tapped and waiting for their code.</param>
    public static IReadOnlyList<OpenQuestionRecord> Select_ToSupersede(
        IEnumerable<OpenQuestionRecord> openInOrchestration,
        DateTime? ownerRepliedUtc,
        IReadOnlyCollection<long> awaitingReadBack)
    {
        if (ownerRepliedUtc == null)
            return [];

        return [.. openInOrchestration.Where(question =>
            question.AskedUtc < ownerRepliedUtc.Value
            && !awaitingReadBack.Contains(question.MessageId))];
    }

    /// <summary>
    /// The open question the new one repeats, or null. A repeat is the same question line — compared
    /// without case and with runs of whitespace collapsed, since a session re-typing its question
    /// reflows it. Only the question line: the options, the deadline and the recommendation are
    /// terms of the same question, and the owner answers the question.
    ///
    /// <para>
    /// Null for a record saved before its question line was remembered (<see cref="OpenQuestionRecord.Prompt"/>
    /// absent): nothing to compare is not a match, and the cost is the old behaviour, one duplicate.
    /// </para>
    /// </summary>
    public static OpenQuestionRecord? Find_Repeat_OrNull(IEnumerable<OpenQuestionRecord> openInOrchestration, string newPrompt)
    {
        var wanted = Normalise(newPrompt);

        if (wanted.Length == 0)
            return null;

        return openInOrchestration.FirstOrDefault(question => question.Prompt != null && Normalise(question.Prompt) == wanted);
    }

    static string Normalise(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}
