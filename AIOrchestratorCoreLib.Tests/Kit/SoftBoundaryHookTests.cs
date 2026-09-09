using System.Diagnostics;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE ADVISORY THAT LETS A LONG TURN CHOOSE ITS OWN ENDING.
///
/// Measured on the production VPS, 2026-09-09: with one fresh session per turn the median member
/// turn is 10 model calls, but the tail runs 60 to 92 and hits the 30-minute deadline — and **41 %
/// of that night's member tokens sat in turns that ran to the deadline**, where the in-turn context
/// had climbed to around 200 k. The closing turn is the hard net; this hook is the chance to stop
/// before the axe, at a point the session picks.
///
/// It ADVISES and never blocks (decision 21), so what has to be proven is exactly four things: it
/// says nothing before the threshold, it speaks once past it, it never speaks to a role whose turn
/// is the owner's phone line, and no malformed input can make it emit anything at all. None of that
/// is readable from C# — the behaviour lives in bash — so this runs the shipped script the way the
/// CLI runs it: real payloads on stdin, the answer read off stdout, in the same style as
/// RunToTheEndHookTests next door.
///
/// Nothing here skips. If bash is missing the test FAILS: a harness that cannot run what it tests
/// must not certify it (decision 20).
/// </summary>
public class SoftBoundaryHookTests : IDisposable
{
    /// <summary>The default the shipped script carries. Restated here so a change to either side is a failing test rather than a silent drift.</summary>
    const int DEFAULT_THRESHOLD = 35;

    const string MAIN_AGENT = "claude";

    readonly string _temp;

    public SoftBoundaryHookTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-softboundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);
    }

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    /// <summary>
    /// THE ORDINARY TURN HEARS NOTHING. The median member turn is 10 calls and 80 % of them are
    /// under 9, so a threshold that fired early would be advising the whole population — which is
    /// how the named risk (short thinking: an agent that knows it may be stopped takes the smaller
    /// decision) would arrive in every turn instead of the top tenth.
    /// </summary>
    [Fact]
    public void NothingIsSaidBeforeTheThreshold()
    {
        var session = New_Session();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Assert.Equal("", Advisory("implementer", session, call));
    }

    /// <summary>The whole point: past the threshold the model is told, in its own context.</summary>
    [Theory]
    [InlineData("implementer")]
    [InlineData("reviewer")]
    [InlineData("solo")]
    public void PastTheThreshold_TheAdvisoryReachesTheModel(string role)
    {
        var session = New_Session();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Run(role, session, call);

        var advisory = Advisory(role, session, DEFAULT_THRESHOLD);

        Assert.Contains("\"hookEventName\":\"PreToolUse\"", advisory);
        Assert.Contains("additionalContext", advisory);
        Assert.Contains("SOFT BOUNDARY", advisory);
        Assert.Contains($"you are {DEFAULT_THRESHOLD} tool calls into this turn", advisory);
    }

    /// <summary>
    /// ONCE PER TURN, NOT ONCE PER CALL PAST THE THRESHOLD. A 92-call turn would otherwise carry 57
    /// copies of the same paragraph — a waterfall, which is the thing this system exists to prevent
    /// (decision 14), and which would drown the advice it is trying to give.
    /// </summary>
    [Fact]
    public void ItSpeaksOnceAndThenNeverAgain()
    {
        var session = New_Session();

        var spoken = 0;

        for (var call = 1; call <= DEFAULT_THRESHOLD + 25; call++)
        {
            if (Advisory("implementer", session, call).Length > 0)
                spoken++;
        }

        Assert.Equal(1, spoken);
    }

    /// <summary>
    /// IT ADVISES, IT NEVER BLOCKS (decision 21: hooks advise, the app enforces at the point of
    /// effect). Asserted on the firing call, because that is the only output there is and a
    /// permissionDecision smuggled into it would deny a live member's tool call.
    /// </summary>
    [Fact]
    public void NothingItEverEmits_CanDenyACall()
    {
        var session = New_Session();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Run(session: session, role: "implementer", call: call);

        var advisory = Advisory("implementer", session, DEFAULT_THRESHOLD);

        Assert.NotEqual("", advisory);
        Assert.DoesNotContain("permissionDecision", advisory);
        Assert.DoesNotContain("\"decision\"", advisory);
        Assert.DoesNotContain("\"continue\"", advisory);
    }

    /// <summary>
    /// THE SUPERVISOR'S TURN IS THE OWNER'S PHONE LINE, and the general supervisor's is their
    /// concierge: advising either to stop and report mid-sentence would interrupt the one
    /// conversation the whole system exists to protect. The empty role is a session outside an
    /// orchestration, which is nobody's business either.
    /// </summary>
    [Theory]
    [InlineData("supervisor")]
    [InlineData("general-supervisor")]
    [InlineData("communicator")]
    [InlineData("")]
    public void TheRolesWhoseTurnIsAConversation_AreExempt(string role)
    {
        var session = New_Session();

        for (var call = 1; call <= DEFAULT_THRESHOLD + 20; call++)
            Assert.Equal("", Advisory(role, session, call));
    }

    /// <summary>
    /// A HOOK THAT CANNOT EVALUATE ITS PREDICATE SAYS SO, AND ALLOWS. Every one of these is a real
    /// shape: a payload the extractor cannot parse, one with no session_id, and a session_id crafted
    /// to walk out of the state folder it becomes a path in.
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"session_id\":\"\"}")]
    [InlineData("{\"session_id\":\"../../etc\"}")]
    [InlineData("{\"session_id\":\"a/b\"}")]
    public void AMalformedPayload_SaysNothingAndAllows(string payload)
    {
        // Threshold 1, so a payload that parsed at all WOULD fire on its first call — without this
        // the cases below would pass by being under the threshold rather than by being refused.
        var (output, exitCode) = Run_Raw("implementer", payload, threshold: "1");

        Assert.Equal("", output);
        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// THE NUMBER IS OVERRIDABLE so the bridge can tune it per role or per stage without a kit
    /// change — and a TYPO MUST NOT TURN THE GUARD OFF, which is why a non-numeric value falls back
    /// to the default instead of disabling anything. `0` is the one explicit OFF.
    /// </summary>
    [Fact]
    public void TheThreshold_IsOverridableByTheEnvironment()
    {
        var early = New_Session();
        Assert.Equal("", Run_Raw("implementer", Payload(early, 1), threshold: "3").Output);
        Assert.Equal("", Run_Raw("implementer", Payload(early, 2), threshold: "3").Output);
        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(early, 3), threshold: "3").Output);

        var off = New_Session();
        for (var call = 1; call <= DEFAULT_THRESHOLD + 5; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(off, call), threshold: "0").Output);

        var typo = New_Session();
        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Assert.Equal("", Run_Raw("implementer", Payload(typo, call), threshold: "lots").Output);

        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(typo, DEFAULT_THRESHOLD), threshold: "lots").Output);
    }

    /// <summary>
    /// A SUB-AGENT'S CALLS COUNT, BUT IT IS NEVER THE ONE ADVISED.
    ///
    /// Measured 2026-09-09 against CLI 2.1.266: a sub-agent's tool call carries the PARENT's
    /// `session_id` and the parent's `transcript_path`, with only `agent_type` naming the sub-agent.
    /// Their calls cost real tokens so they belong in the count; but telling a read-only Explore
    /// agent to commit and write a report is nonsense, and it would burn the once-per-turn latch on
    /// the wrong recipient — silencing the advisory in exactly the fan-out-heavy turns it exists
    /// for, which decision 16 makes an implementer's default.
    /// </summary>
    [Fact]
    public void ASubAgentsCallsCount_ButTheAdvisoryIsNeverGivenToIt()
    {
        var session = New_Session();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Run("implementer", session, call);

        Assert.Equal("", Run_Raw("implementer", Payload(session, DEFAULT_THRESHOLD, agentType: "Explore")).Output);
        Assert.Contains("SOFT BOUNDARY", Run_Raw("implementer", Payload(session, DEFAULT_THRESHOLD + 1)).Output);
    }

    /// <summary>
    /// TWO TURNS ARE TWO SESSIONS. In fresh mode one CLI session is one turn, which is the whole
    /// reason the counter is keyed on `session_id`: a member that has already been advised once must
    /// start from zero the next time the bridge wakes it, or a long endeavour would hear the line
    /// exactly once and never again.
    /// </summary>
    [Fact]
    public void ANewSessionStartsCountingFromZero()
    {
        var first = New_Session();

        for (var call = 1; call <= DEFAULT_THRESHOLD; call++)
            Run("implementer", first, call);

        var second = New_Session();

        Assert.Equal("", Advisory("implementer", second, 1));

        for (var call = 2; call < DEFAULT_THRESHOLD; call++)
            Run("implementer", second, call);

        Assert.Contains("SOFT BOUNDARY", Advisory("implementer", second, DEFAULT_THRESHOLD));
    }

    /// <summary>
    /// The one thing that cannot be proven by running it: the plugin declares the hook as
    /// `bash "${CLAUDE_PLUGIN_ROOT}/hooks/…"`, so it does not need the executable bit to RUN — but
    /// the kit ships every script 100755 and a test enforces it for the helpers, because the day one
    /// of them is invoked directly a 100644 file is a silent no-op (measured 2026-09-06 on
    /// channel-append.sh: `which` printed NOT FOUND and a supervisor wrote its channel unlocked).
    /// Skipped where the mode does not exist rather than asserted falsely.
    /// </summary>
    [Fact]
    public void TheShippedHook_IsExecutable()
    {
        var hook = Find_Hook_OrFail();

        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(hook);

        Assert.True(
            mode.HasFlag(UnixFileMode.UserExecute) && mode.HasFlag(UnixFileMode.GroupExecute) && mode.HasFlag(UnixFileMode.OtherExecute),
            $"{hook} is not 100755 — every executable in the kit ships with the bit set, and the git index is what carries it to the VPS");
    }

    /// <summary>
    /// THE ADVISORY IS HALF THE MECHANISM, and the half that can go wrong quietly. The named risk in
    /// the design's own risk table is SHORT THINKING — an agent that knows it may be stopped takes
    /// the smaller decision — so the words must forbid that at least as loudly as they ask for the
    /// stop. A rewrite that drops the second half would still pass every behavioural test above.
    /// </summary>
    [Fact]
    public void TheWords_TellTheModelNotToStopShort()
    {
        var session = New_Session();

        for (var call = 1; call < DEFAULT_THRESHOLD; call++)
            Run("implementer", session, call);

        var advisory = Advisory("implementer", session, DEFAULT_THRESHOLD);

        Assert.Contains("nothing is blocked", advisory);
        Assert.Contains("FINISH THAT CHANGE FIRST", advisory);
        Assert.Contains("never stop before something is verifiable", advisory);
        Assert.Contains("DO NOT TAKE A SMALLER DECISION", advisory);
        Assert.Contains("Do not narrow what you were asked to do", advisory);
    }

    static string New_Session() => Guid.NewGuid().ToString();

    static string Payload(string session, int call, string agentType = MAIN_AGENT)
    {
        return "{\"session_id\":\"" + session + "\",\"agent_type\":\"" + agentType
            + "\",\"tool_use_id\":\"toolu_" + call.ToString("D4") + "\",\"tool_name\":\"Bash\",\"hook_event_name\":\"PreToolUse\"}";
    }

    void Run(string role, string session, int call) => Run_Raw(role, Payload(session, call));

    string Advisory(string role, string session, int call) => Run_Raw(role, Payload(session, call)).Output;

    /// <summary>
    /// Runs the SHIPPED script the way the CLI runs it — payload on stdin, verdict on stdout — with
    /// TMPDIR pointed at this test's own folder so the per-session call counter it keeps is
    /// hermetic and nothing outside the temp folder is touched.
    /// </summary>
    (string Output, int ExitCode) Run_Raw(string role, string payload, string? threshold = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Bash_Locator.Find_OrFail(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(Find_Hook_OrFail().Replace('\\', '/'));

        startInfo.Environment["TMPDIR"] = _temp;
        startInfo.Environment["AIORCH_ROLE"] = role;

        // No AIORCH_ID: the advisory needs no orchestration folder, and the log-undecidable helper
        // deliberately says nothing when there is no orchestration to tell.
        startInfo.Environment.Remove("AIORCH_ID");

        if (threshold != null)
            startInfo.Environment["AIORCH_SOFT_BOUNDARY_CALLS"] = threshold;
        else
            startInfo.Environment.Remove("AIORCH_SOFT_BOUNDARY_CALLS");

        using var process = Process.Start(startInfo) ?? throw new Exception("could not start bash");

        // A BROKEN PIPE HERE IS THE HOOK BEING FAST, NOT THE HOOK BEING WRONG. The exempt-role and
        // threshold-off checks come BEFORE the script reads stdin — every hook in this kit is
        // arranged that way, so the cheapest path costs one `case` — which means bash can exit while
        // this write is still in flight. It is a race: the same four cases passed on a filtered run
        // and failed on the full suite, purely on timing. The payload not arriving is exactly what
        // those cases assert, so the write is allowed to fail and the verdict is still read.
        try
        {
            process.StandardInput.Write(payload);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(60_000))
            throw new Exception("the hook did not exit within 60 s");

        return (output.Trim(), process.ExitCode);
    }

    static string Find_Hook_OrFail()
    {
        return KitRepoFiles.Find(Path.Combine("kit", "hooks", "soft-boundary-check.sh"))
            ?? throw new Exception("kit/hooks/soft-boundary-check.sh was not found — REFUSING to pass about a hook this test never located.");
    }
}
