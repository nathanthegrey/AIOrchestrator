namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// Finds a file or folder in the repo from the test binary's own output folder. ONE copy of the
/// walk-up that six Kit tests each carried privately — they all had to change together when the kit
/// moved from kit/commands/&lt;role&gt;.md to kit/skills/&lt;role&gt;/SKILL.md, which is the argument for
/// having one.
///
/// Returns null rather than a guess. Every caller turns that null into a refusal to run, because a
/// content test that found no content passes by finding nothing.
/// </summary>
public static class KitRepoFiles
{
    const int MAX_DEPTH = 8;

    public static string? Find(string relativePath)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < MAX_DEPTH; depth++)
        {
            var candidate = Path.Combine(folder, relativePath);

            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return null;
    }

    /// <summary>The protocol file of one role, at its plugin location.</summary>
    public static string? Find_RoleProtocol(string role)
    {
        return Find(Path.Combine("kit", "skills", role, "SKILL.md"));
    }

    /// <summary>Every role protocol in the kit, as (role, path) — the role is the FOLDER now, not the filename.</summary>
    public static IReadOnlyList<(string Role, string Path)> Find_AllRoleProtocols()
    {
        var skills = Find(Path.Combine("kit", "skills"));

        if (skills == null)
            return [];

        return [.. Directory.GetDirectories(skills)
            .Select(folder => (Role: System.IO.Path.GetFileName(folder), Path: System.IO.Path.Combine(folder, "SKILL.md")))
            .Where(entry => File.Exists(entry.Path))
            .OrderBy(entry => entry.Role)];
    }
}
