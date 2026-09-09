using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Translation.MessageTranslator;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Translation;

/// <summary>
/// THE RETRY IS GONE (owner directive 2026-09-09). A failed EN→IT call used to fire a SECOND
/// `claude -p` process before giving up — doubling the process-spawn cost of every failure for a
/// translator that logged 183 failures in one recent run. The Italian layer is opt-in now (default
/// off), so a rare failure falls back to the original text after exactly ONE attempt; it no longer
/// pays twice to rescue an opt-in feature.
///
/// <para>
/// These fake the `claude` binary itself by shadowing it on PATH for the duration of the test — the
/// only way to observe how many times <c>MessageTranslatorModel</c> actually shells out, since the
/// class is `internal sealed` and reached only through <see cref="IMessageTranslator"/>.
/// </para>
/// </summary>
public sealed class MessageTranslatorModelTests : IDisposable
{
    const string PROSE =
        "Both current sessions are mid-task, so one more has been asked to take it rather than let it queue.";

    readonly string _tempDir = "";
    readonly string _callLog = "";
    readonly string? _originalPath;
    readonly CollectingLog_Fake _log = new();
    readonly IMessageTranslator? _translator;

    readonly bool _skipped = OperatingSystem.IsWindows();

    public MessageTranslatorModelTests()
    {
        // POSIX ONLY, ON PURPOSE. MessageTranslatorModel resolves the shell per OS
        // (ShellCommand_Builder); this fake shadows the "claude" name on PATH for /bin/sh, which is
        // how the CLI resolves everywhere except Windows (a claude.cmd shim there). This repo's dev
        // machine and VPS are both POSIX, so the gap is Windows CI only — skipped there rather than
        // asserted falsely, same as TheAppendHelper_IsExecutable_OrBinOnPathBuysNothing.
        if (_skipped)
            return;

        _tempDir = Path.Combine(Path.GetTempPath(), $"aiorch-fake-claude-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _callLog = Path.Combine(_tempDir, "calls.log");

        // A FAKE `claude` THAT ALWAYS FAILS. Every attempt appends one line to _callLog and exits
        // non-zero, so a call COUNT is the only thing that can tell one attempt from a retried one.
        var scriptPath = Path.Combine(_tempDir, "claude");
        File.WriteAllText(scriptPath, "#!/bin/sh\necho call >> \"$FAKE_CLAUDE_CALL_LOG\"\nexit 1\n");

        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        // Shadow the REAL `claude` on PATH — this test must never spawn the actual CLI (cost, and a
        // 45 s hang risk). Prepending only shadows the literal name "claude"; every other lookup on
        // PATH keeps resolving exactly as before.
        _originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", $"{_tempDir}{Path.PathSeparator}{_originalPath}");
        Environment.SetEnvironmentVariable("FAKE_CLAUDE_CALL_LOG", _callLog);

        _translator = MessageTranslator_Factory.Create(_log);
    }

    public void Dispose()
    {
        if (_skipped)
            return;

        Environment.SetEnvironmentVariable("PATH", _originalPath);
        Environment.SetEnvironmentVariable("FAKE_CLAUDE_CALL_LOG", null);

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort — a temp folder is not precious.
        }
    }

    [Fact]
    public async Task AFailedItalianTranslation_MakesExactlyOneAttempt()
    {
        if (_skipped)
            return;

        await _translator!.Translate_ToItalian_Async(PROSE, CancellationToken.None);

        var attempts = File.Exists(_callLog) ? File.ReadAllLines(_callLog).Length : 0;

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AFailedItalianTranslation_FallsBackToTheOriginalText()
    {
        if (_skipped)
            return;

        var result = await _translator!.Translate_ToItalian_Async(PROSE, CancellationToken.None);

        Assert.Contains(PROSE, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedEnglishTranslation_AlsoMakesExactlyOneAttempt()
    {
        if (_skipped)
            return;

        await _translator!.Translate_ToEnglish_Async(PROSE, CancellationToken.None);

        var attempts = File.Exists(_callLog) ? File.ReadAllLines(_callLog).Length : 0;

        Assert.Equal(1, attempts);
    }
}

internal sealed class CollectingLog_Fake : IOrchestrationLog
{
    public List<(string OrchId, string Message)> Warnings { get; } = [];

    public void Log_Info(string orchId, string message)
    {
    }

    public void Log_Warning(string orchId, string message)
    {
        Warnings.Add((orchId, message));
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
    }

    public event Action<IOrchestrationLogEntry>? EntryLogged
    {
        add { }
        remove { }
    }
}
