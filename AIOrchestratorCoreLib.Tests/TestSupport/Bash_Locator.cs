namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// The ONE answer to "where is bash" for every test that runs a kit script. Two private copies
/// used to list Windows paths only, so on macOS and Linux the whole bash half of the suite — the
/// append helper, the hooks — failed before running anything, which certified nothing about the
/// kit on the OSes the daemon exists for. Windows candidates stay first: nothing changes there.
///
/// Fails loudly rather than returning null: a harness that cannot find what it tests must refuse
/// to run (decision 20), never quietly pass.
/// </summary>
public static class Bash_Locator
{
    static readonly string[] CANDIDATES =
    [
        @"C:\Program Files\Git\bin\bash.exe",
        @"C:\Program Files\Git\usr\bin\bash.exe",
        @"C:\Windows\System32\bash.exe",
        "/bin/bash",
        "/usr/bin/bash",
        "/usr/local/bin/bash",
        "/opt/homebrew/bin/bash",
    ];

    public static string Find_OrFail()
    {
        foreach (var candidate in CANDIDATES)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new Exception(
            $"No bash found at any of: {string.Join(", ", CANDIDATES)}. The kit's scripts are bash and cannot be checked without it; "
            + "passing without running them is the failure mode these tests exist to avoid.");
    }
}
