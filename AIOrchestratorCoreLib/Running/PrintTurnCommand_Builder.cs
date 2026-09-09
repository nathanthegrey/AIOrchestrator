using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Spawning;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// The argument list of one print turn — the contract the Live tests pin:
/// <c>-p --output-format json --name &lt;name&gt; (--session-id | --resume) &lt;id&gt;
/// [--max-budget-usd n] [--model m] [--settings f] (--permission-mode m | --dangerously-skip-permissions)
/// [--disallowedTools Write Edit NotebookEdit --] [&lt;role command&gt;]</c>.
///
/// A session's FIRST turn (and every turn in Fresh mode) is booted by its role command as the
/// positional prompt, exactly as the terminal spawn has always done; later turns resume the
/// transcript and take their prompt on STDIN, so no channel text ever meets a shell. The
/// reviewer's tool denial and its <c>--</c> terminator come from <see cref="SpawnCommand_Builder"/>'s
/// lesson: the variadic flag swallowed a prompt once.
/// </summary>
public static class PrintTurnCommand_Builder
{
    public static readonly IReadOnlyList<string> REVIEWER_DISALLOWED_TOOLS = ["Write", "Edit", "NotebookEdit"];

    /// <summary>The <c>--name</c> a print session runs under — visible in <c>claude agents --json</c> while a turn runs.</summary>
    public static string Build_SessionName(IPrintSessionState state)
    {
        return state.Role == SessionRoles.General ? SessionLaunch.SessionLaunch_Factory.GENERAL_MEMBER_ID : $"{state.OrchId}-{state.MemberId}";
    }

    /// <summary>
    /// THE CLOSING TURN'S SHAPE, from the same builder as every other print turn — the only turn
    /// that carries a spend cap, and a resume by definition: the transcript it closes down is the one
    /// the deadline killed. Print-shaped even for a stream session, because that session's living
    /// process was killed with it and <c>--max-budget-usd</c> works only with <c>--print</c>
    /// (verified in <c>claude --help</c>, 2.1.266).
    /// </summary>
    public static IReadOnlyList<string> Build_ClosingTurnArguments(IPrintSessionState state, IRoleRunnerConfig roleConfig, string resumeSessionId, string? settingsFile)
    {
        return Build_Arguments(state, roleConfig, resumeSessionId, resumeTranscript: true, settingsFile, ClosingTurn.ClosingTurn_Words.BUDGET_USD);
    }

    /// <param name="maxBudgetUsd">
    /// Null on every ordinary turn — the ceiling that governs those is the turn timeout and the
    /// memory sandbox. Non-null only for the closing turn, whose whole job is one report.
    /// </param>
    public static IReadOnlyList<string> Build_Arguments(IPrintSessionState state, IRoleRunnerConfig roleConfig, string sessionId, bool resumeTranscript, string? settingsFile, double? maxBudgetUsd = null)
    {
        List<string> arguments = ["-p", "--output-format", "json", "--name", Build_SessionName(state)];

        arguments.Add(resumeTranscript ? "--resume" : "--session-id");
        arguments.Add(sessionId);

        // BEFORE the reviewer's variadic --disallowedTools and its `--` terminator, never after: a
        // flag added past the terminator is read as the positional prompt, which is the lesson that
        // put the terminator there in the first place.
        if (maxBudgetUsd != null)
        {
            arguments.Add(ClosingTurn.ClosingTurn_Words.BUDGET_FLAG);
            arguments.Add(ClosingTurn.ClosingTurn_Words.Describe_Budget(maxBudgetUsd.Value));
        }

        if (!string.IsNullOrWhiteSpace(state.Model))
        {
            arguments.Add("--model");
            arguments.Add(state.Model);
        }

        if (!string.IsNullOrWhiteSpace(settingsFile))
        {
            // --resume does not restore --settings (MEASUREMENTS.md): passed on every turn.
            arguments.Add("--settings");
            arguments.Add(settingsFile);
        }

        if (roleConfig.PermissionMode != null)
        {
            arguments.Add("--permission-mode");
            arguments.Add(roleConfig.PermissionMode);
        }
        else
        {
            arguments.Add(SpawnCommand_Builder.CLAUDE_LAUNCH_FLAGS);
        }

        if (state.Role == SessionRoles.Reviewer)
        {
            arguments.Add("--disallowedTools");
            arguments.AddRange(REVIEWER_DISALLOWED_TOOLS);
            arguments.Add("--");
        }

        if (!resumeTranscript)
            arguments.Add(SessionRole_Names.Build_RoleCommand(state.Role, state.OrchId, state.MemberId));

        return arguments;
    }
}
