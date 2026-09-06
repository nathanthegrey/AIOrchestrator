using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Reads what a Claude home says about an installed plugin, from the two files that hold the answer:
///
///   plugins/installed_plugins.json   version + installPath, per scope
///   settings.json  -> enabledPlugins  whether it is switched on
///
/// FILES, NOT `claude plugin list --json`. Shelling out would make the host's startup depend on a
/// CLI being on PATH and on its output format, and would make this untestable without a real
/// installation; both files are stable on-disk state that a temp folder can reproduce exactly.
///
/// A file that is absent is an ANSWER (not installed / not enabled). A file that is present but
/// unreadable is NOT an answer, and is reported as such rather than being read as absence — that
/// distinction is the whole point: absence and illegibility must not collapse into the same verdict.
/// </summary>
public static class InstalledPlugin_Reader
{
    public const string INSTALLED_PLUGINS_FILE = "installed_plugins.json";
    public const string PLUGINS_FOLDER = "plugins";
    public const string SETTINGS_FILE = "settings.json";
    public const string ENABLED_PLUGINS_KEY = "enabledPlugins";

    /// <summary><paramref name="claudeHomeFolder"/> is the folder holding settings.json and plugins/.</summary>
    public static InstalledPluginReading Read(string claudeHomeFolder, string pluginId)
    {
        var installedFile = Path.Combine(claudeHomeFolder, PLUGINS_FOLDER, INSTALLED_PLUGINS_FILE);
        var settingsFile = Path.Combine(claudeHomeFolder, SETTINGS_FILE);

        string? version = null;
        string? installPath = null;

        if (File.Exists(installedFile))
        {
            JsonNode? root;

            try
            {
                root = JsonNode.Parse(File.ReadAllText(installedFile));
            }
            catch (Exception ex)
            {
                return new InstalledPluginReading(null, null, false, $"{installedFile} could not be read: {ex.Message}");
            }

            // Shape (measured on a real install): { "version": 2, "plugins": { "<id>": [ { scope,
            // installPath, version, ... } ] } } — an ARRAY per id, because one plugin can be
            // installed at more than one scope. First record wins; the refusal names the path, so a
            // human who has two can see which one answered.
            if (root is not JsonObject installedRoot)
                return new InstalledPluginReading(null, null, false, $"{installedFile} is not a JSON object");

            var records = installedRoot["plugins"]?[pluginId] as JsonArray;
            var first = records?.FirstOrDefault() as JsonObject;

            version = first?["version"]?.GetValue<string>();
            installPath = first?["installPath"]?.GetValue<string>();
        }

        var enabled = false;

        if (File.Exists(settingsFile))
        {
            try
            {
                var settings = JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject;
                enabled = settings?[ENABLED_PLUGINS_KEY]?[pluginId]?.GetValue<bool>() ?? false;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                return new InstalledPluginReading(version, installPath, false, $"{settingsFile} could not be read: {ex.Message}");
            }
        }

        return new InstalledPluginReading(version, installPath, enabled, null);
    }
}
