namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW a session is run. <see cref="Terminal"/> is the shape this app has always had: a Windows
/// Terminal window per session, kept alive between messages and woken by its own watcher.
/// <see cref="Print"/> is the transient shape measured in the 2026-09-05 deep study: no process
/// between messages, every inbound entry becomes one <c>claude -p --resume</c> invocation, and the
/// bridge writes the session's channel entry from the JSON result. The default is Terminal
/// everywhere, so nothing changes until the owner changes it in config.json.
/// </summary>
public enum SessionRunners
{
    Terminal,
    Print,
}

public static class SessionRunner_Names
{
    public const string TERMINAL = "terminal";
    public const string PRINT = "print";

    public static string Get_Word(SessionRunners runner)
    {
        return runner switch
        {
            SessionRunners.Terminal => TERMINAL,
            SessionRunners.Print => PRINT,
            _ => throw new Exception($"Unhandled SessionRunners: {runner}"),
        };
    }

    /// <summary>Null for an unknown word — the caller decides the default, this does not guess.</summary>
    public static SessionRunners? Parse_OrNull(string? word)
    {
        return word?.Trim().ToLowerInvariant() switch
        {
            TERMINAL => SessionRunners.Terminal,
            PRINT => SessionRunners.Print,
            _ => null,
        };
    }
}
