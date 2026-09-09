using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Processes;

namespace AIOrchestratorCoreLib.Translation.MessageTranslator;

/// <summary>
/// Shells out to the Claude Code CLI headless (`claude -p --model haiku`) — no API key needed, it
/// rides the owner's plan like every session does. Instruction AND content travel via stdin so no
/// text ever needs command-line escaping. Any failure, timeout, or empty output falls back to the
/// original text — a broken translator must never block Telegram traffic.
/// </summary>
internal sealed class MessageTranslatorModel(IOrchestrationLog log) : IMessageTranslator
{
    /// <summary>Inbound IT→EN: haiku suffices (already-English text just passes through).</summary>
    const string TO_ENGLISH_MODEL = "haiku";

    /// <summary>Outbound EN→IT is owner-facing and quality-critical — haiku repeatedly returned English or mixed prose.</summary>
    const string TO_ITALIAN_MODEL = "sonnet";

    const int TRANSLATION_TIMEOUT_MILLISECONDS = 45_000;

    const string TO_ENGLISH_INSTRUCTION =
        "You are a translation filter. Translate the message below to English. "
        + "If it is already entirely in English, return it UNCHANGED. "
        + "Preserve formatting, line breaks, bullets and emoji exactly. "
        + "Preserve any line starting with 'IMAGE:' exactly as written. "
        + "Reply with ONLY the translated text - no preamble, no quotes, no commentary.";

    const string TO_ITALIAN_INSTRUCTION =
        "You are a translation filter. Translate the message below to Italian. "
        + "Translate ALL the prose - never leave whole sentences in English. "
        // THE LIST IS CLOSED ON PURPOSE. It used to end with "hardware terms, product names, code
        // identifiers, file paths and error messages" — an open invitation the model extended to
        // ordinary English words, so the owner received Italian salted with "un-park", "mid-task",
        // "row", "browser pass" and "findings". Those are not terms an Italian professional uses;
        // they are the translator declining to translate. Anything not named below is prose.
        + "But keep these technical TERMS in English exactly as written, the way Italian professionals "
        + "use them, and ONLY these: "
        + "financial/trading terms (Long, Short, spread, hedge ratio, backtest, drawdown); "
        + "programming terms (commit, merge, branch, worktree, build, test, bug, refactor, deploy, review, grep); "
        + "product names; file paths; code identifiers (anything with a dot, slash, underscore or CamelCase); "
        + "and verbatim error messages. "
        + "Every other English word is prose and MUST be translated, including project jargon. "
        + "Preserve formatting, line breaks, bullets and emoji exactly. "
        + "Reply with ONLY the translated text - no preamble, no quotes, no commentary.";

    readonly IOrchestrationLog _log = log;

    public async Task<string> Translate_ToEnglish_Async(string text, CancellationToken cancellationToken)
    {
        return await Translate_Async(TO_ENGLISH_INSTRUCTION, text, TO_ENGLISH_MODEL, cancellationToken);
    }

    public async Task<string> Translate_ToItalian_Async(string text, CancellationToken cancellationToken)
    {
        var translated = await Translate_Async(TO_ITALIAN_INSTRUCTION, text, TO_ITALIAN_MODEL, cancellationToken);

        if (translated != text)
            return translated;

        // ONE ATTEMPT ONLY (owner directive 2026-09-09). A retry used to follow every failure — a
        // second `claude -p` process stacked on the first — which doubled the spawn cost of exactly
        // the failures the log was full of (183 in one recent run). The Italian layer is opt-in now
        // (default off, see OrchestratorConfig_Factory.DEFAULT_TELEGRAM_ITALIAN_LAYER), so a rare
        // failure falls back to the original text straight away; it no longer pays twice to rescue
        // a feature the owner is not using by default.
        //
        // THE OWNER IS STILL TOLD. A log warning is not a channel to the person holding the phone:
        // they received English in the middle of an Italian conversation with nothing to say why,
        // and read it as a deliberate switch of language. See UntranslatedText_Marker for why only
        // prose is marked.
        if (!UntranslatedText_Marker.Should_Mark(translated))
            return translated;

        _log.Log_Warning("", $"EN→IT translation failed ({text.Length} chars) — the owner is getting it in English, marked");

        return UntranslatedText_Marker.Mark(translated);
    }

    async Task<string> Translate_Async(string instruction, string text, string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            using var process = Process.Start(Build_StartInfo(model))
                ?? throw new Exception("Process.Start returned null for the claude CLI");

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TRANSLATION_TIMEOUT_MILLISECONDS);

            await process.StandardInput.WriteAsync($"{instruction}\n\nMESSAGE:\n{text}");
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            var errorDrainTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                Kill_BestEffort(process);
                _log.Log_Warning("", "Translation timed out — using the original text");
                return text;
            }

            var output = (await outputTask).Trim();
            await errorDrainTask;

            if (process.ExitCode != 0 || output.Length == 0)
            {
                _log.Log_Warning("", $"Translation call failed (exit code {process.ExitCode}, output length {output.Length}) — using the original text");
                return text;
            }

            return output;
        }
        catch (Exception ex)
        {
            _log.Log_Warning("", $"Translation failed — using the original text: {ex.Message}");
            return text;
        }
    }

    /// <summary>
    /// The OS shell resolves claude through PATH (cmd /c honours .cmd shims on Windows, /bin/sh -c
    /// elsewhere) — the host app has no shell of its own. See <see cref="ShellCommand_Builder"/>.
    /// </summary>
    static ProcessStartInfo Build_StartInfo(string model)
    {
        var startInfo = ShellCommand_Builder.Build_StartInfo($"claude -p --model {model}");
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardInputEncoding = new UTF8Encoding(false);
        startInfo.StandardOutputEncoding = new UTF8Encoding(false);
        startInfo.StandardErrorEncoding = new UTF8Encoding(false);
        return startInfo;
    }

    static void Kill_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited between the timeout and the kill — nothing to do.
        }
    }
}
