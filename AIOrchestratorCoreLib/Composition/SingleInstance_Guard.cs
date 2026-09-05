using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Composition;

/// <summary>
/// One running host per supervision root. The WPF app used a named mutex, which exists only on
/// Windows; this is the cross-platform equivalent and now the only mechanism, so the app and the
/// daemon exclude EACH OTHER too — two pollers on one bot token is the failure both are guarding
/// against, whichever host the second one is.
///
/// The lock is an exclusive open (FileShare.None) on a file under the root: on Windows that is a
/// kernel share lock, on Linux/macOS .NET maps it to an advisory flock — both released by the OS
/// the instant the process dies, so a crashed host never leaves a stale lock behind, which a
/// pid-in-a-file scheme would.
/// </summary>
public static class SingleInstance_Guard
{
    /// <summary>The held lock, or null when another host already holds it.</summary>
    public static IDisposable? Try_Acquire(ISupervisionPaths paths)
    {
        Directory.CreateDirectory(paths.Root);

        try
        {
            return new FileStream(
                paths.InstanceLockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            // Sharing violation on Windows, EWOULDBLOCK on POSIX: the other host is alive.
            return null;
        }
    }
}
