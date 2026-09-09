using System.Globalization;

namespace AIOrchestratorCoreLib.Running.ClosingTurn;

/// <summary>
/// The public constants of the closing turn — the one short turn a session gets after its work
/// turn was killed at the deadline. Spelled once here because three files need them: the command
/// builder (the budget flag), the dispatcher (the request-id suffix and the timeout) and the tests.
///
/// <para>
/// MEASURED, WHICH IS WHY THIS EXISTS AT ALL. On the VPS between 2026-09-07 and 09, 28 turns were
/// killed by the 30-minute turn timeout (<c>RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT</c>) and had
/// their pending entries re-run from scratch after the 60-second backoff: roughly 14 hours of work
/// discarded. Those turns were WORKING — 32 to 92 tool calls, 7 to 30 M input tokens — not hung; a
/// hung process is caught much earlier and much cheaper by the silence limit. Raising the deadline
/// was refused (a longer turn is a bigger context, which is the bill this stage exists to cut), so
/// the killed transcript is resumed ONCE for long enough to say where it got to, and the next turn
/// starts fresh from the channel. Design: docs/superpowers/specs/2026-09-08-token-efficiency-design.md
/// section C1.3.
/// </para>
/// </summary>
public static class ClosingTurn_Words
{
    /// <summary>
    /// The CLI flag that makes the cap real rather than advisory. Verified present in
    /// <c>claude --help</c> on 2.1.266 (2026-09-09): "<c>--max-budget-usd &lt;amount&gt;</c> Maximum
    /// dollar amount to spend on API calls (only works with --print)" — which is why the closing
    /// turn is print-shaped even for a stream session, quite apart from the process being dead.
    /// </summary>
    public const string BUDGET_FLAG = "--max-budget-usd";

    /// <summary>
    /// Two dollars. Not a budget that buys work — it buys ONE report on work already done, and a
    /// closing turn that spends more than this is doing something it was told not to do. The net
    /// under the prompt, per spec section C9.
    /// </summary>
    public const double BUDGET_USD = 2.00;

    /// <summary>
    /// Five minutes. A report of where a turn got to is not work, and the dispatcher clamps this to
    /// the configured turn timeout when THAT is shorter — a deployment whose turns run for a minute
    /// does not want a closing turn allowed to run for five.
    /// </summary>
    public static readonly TimeSpan TIMEOUT = TimeSpan.FromMinutes(5);

    /// <summary>
    /// What the closing turn's request id ends with: <c>&lt;orch&gt;/&lt;member&gt;/&lt;turn&gt;-closing</c>.
    /// It is NOT a turn number of its own — the killed turn keeps that — so the state file gains no
    /// second record, while the turn log and the <c>turn_ended</c> entry both say which turn this was.
    /// </summary>
    public const string REQUEST_ID_SUFFIX = "closing";

    /// <summary>
    /// THE WHOLE INSTRUCTION, and every clause of it is load-bearing. "Do not continue the task"
    /// because a resumed transcript's most natural move is to carry on, which is what the budget and
    /// the short timeout would then pay for; "ONE short entry" because the entry is mirrored to the
    /// owner's phone; "what is half-done (files, commands)" because git and the channel already hold
    /// what is finished — the intention is the one thing they cannot reconstruct (spec C1.2).
    /// </summary>
    public const string PROMPT =
        "Your previous turn was stopped at the time limit. Do not continue the task. " +
        "In ONE short entry, write to your channel(s) where you are: what is done, what is half-done (files, commands), " +
        "what the next step would be. Then stop.";

    /// <summary>The budget as the CLI is given it — invariant, two decimals, so a comma locale cannot send "2,00".</summary>
    public static string Describe_Budget(double amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>The request id of the closing turn that follows the turn <paramref name="requestId"/> names.</summary>
    public static string Build_RequestId(string requestId)
    {
        return $"{requestId}-{REQUEST_ID_SUFFIX}";
    }
}
