using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WC2.Shared.Configuration;

/// <summary>
/// Loads /configs/*.json living NEXT TO the framework (not inside any plugin folder),
/// so all modules share one config directory and hot-reload works uniformly.
/// Writes a commented default file on first run so admins never edit code.
/// </summary>
public sealed class JsonConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _configDir;
    private readonly ILogger _logger;

    public JsonConfigStore(string moduleDirectory, ILogger logger)
    {
        // <csgo>/addons/counterstrikesharp/plugins/<Module>/../../wc2-configs
        _configDir = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "wc2-configs"));
        Directory.CreateDirectory(_configDir);
        _logger = logger;
    }

    public T LoadOrCreate<T>(string fileName, Func<T> defaultFactory) where T : class
    {
        var path = Path.Combine(_configDir, fileName);
        try
        {
            if (!File.Exists(path))
            {
                var def = defaultFactory();
                File.WriteAllText(path, JsonSerializer.Serialize(def, Options));
                _logger.LogInformation("[WC2] Wrote default config {File}", path);
                return def;
            }
            var loaded = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            if (loaded is not null) return loaded;
            _logger.LogWarning("[WC2] Config {File} was empty, using defaults", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WC2] Failed reading {File}; using defaults (file left untouched)", fileName);
        }
        return defaultFactory();
    }
}
