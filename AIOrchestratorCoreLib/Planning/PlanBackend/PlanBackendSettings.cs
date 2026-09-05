namespace AIOrchestratorCoreLib.Planning.PlanBackend;

/// <summary>
/// Which plan backend this machine runs, from config.json's <c>planBackend</c> object.
///
/// <para>
/// THREE FIELDS AND NO PROTOCOL. The app does not know what an external backend talks to, where it
/// lives, or what it needs — an adapter is a .NET type implementing <see cref="IPlanBackend"/> and
/// reads its own configuration from its own place. Putting a URL or a token here would put another
/// system's contract in this repository's config schema, which is exactly the coupling the seam
/// exists to prevent.
/// </para>
/// </summary>
/// <param name="Kind"><see cref="KIND_PLAN_MD"/> (the default, PLAN.md alone) or <see cref="KIND_EXTERNAL"/>.</param>
/// <param name="AssemblyPath">Path to the adapter's .dll. Required, and used only, when the kind is external.</param>
/// <param name="TypeName">Full name of the type inside it. Required, and used only, when the kind is external.</param>
public readonly record struct PlanBackendSettings(string Kind, string? AssemblyPath, string? TypeName)
{
    public const string KIND_PLAN_MD = "plan-md";
    public const string KIND_EXTERNAL = "external";

    /// <summary>An absent key, an unreadable one, or a kind nobody recognises all mean PLAN.md alone.</summary>
    public static PlanBackendSettings Default()
    {
        return new PlanBackendSettings(KIND_PLAN_MD, null, null);
    }

    public bool Is_External()
    {
        return string.Equals(Kind, KIND_EXTERNAL, StringComparison.OrdinalIgnoreCase);
    }
}
