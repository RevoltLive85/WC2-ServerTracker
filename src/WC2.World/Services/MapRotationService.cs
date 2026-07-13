using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace WC2.World.Services;

/// <summary>
/// Workshop-aware map rotation. Vanilla CS2 rotation cannot cycle workshop maps;
/// this counts rounds and drives the change itself:
///   workshop entries → ds_workshop_changelevel <map>   (hosted collection)
///   stock entries    → changelevel <map>
/// Sequential cycle, skipping the current map so we never "rotate" in place.
/// </summary>
public sealed class MapRotationService
{
    private readonly ILogger _logger;
    private RegionsFileConfig _config;
    private int _roundsOnMap;
    private int _cursor;
    private bool _changePending;
    private bool _finaleHoldActive;

    /// <summary>Hold the round-end rotation while the finale boss fight is still live.</summary>
    public void SetFinaleHold(bool active) => _finaleHoldActive = active;
    public MapRotationService(RegionsFileConfig config, ILogger logger)
    { _config = config; _logger = logger; }

    public void ApplyConfig(RegionsFileConfig config) => _config = config;

    /// <summary>True when the round now STARTING is the map's last (rounds counted on round END,
    /// so at the start of round N the counter reads N-1).</summary>
    public bool IsFinaleRoundStarting() =>
        _config.Rotation.Enabled && _config.Rotation.FinaleEnabled &&
        _config.Rotation.Maps.Count > 0 && !_changePending &&
        _roundsOnMap == Math.Max(1, _config.Rotation.RoundsPerMap) - 1;

    public float FinaleHealthMultiplier => _config.Rotation.FinaleHealthMultiplier;
    public string FinaleFallbackBossId => _config.Rotation.FinaleFallbackBossId;
    public string? FinaleForcedBossId => _config.Rotation.FinaleForcedBossId;
    public float FinaleDurationSeconds => _config.Rotation.FinaleDurationSeconds;
    public int NormalBotQuota => _config.Rotation.NormalBotQuota;

    /// <summary>Rotate soon (finale boss killed → don't idle out the round timer).
    /// Respects a change already scheduled by the round counter.</summary>
    public string? RequestRotation(BasePlugin plugin, float delaySeconds)
    {
        if (_changePending) return null;
        var next = PickNext();
        if (next is null) return null;
        _changePending = true;
        plugin.AddTimer(Math.Max(3f, delaySeconds), () => Execute(next));
        return next.DisplayName ?? next.Map;
    }

    public void OnMapStart()
    {
        _roundsOnMap = 0;
        _changePending = false;
    }

    /// <summary>Called on round end. Returns the upcoming map if a change was scheduled
    /// (so the caller can announce it), otherwise null.</summary>
    public string? OnRoundEnd(BasePlugin plugin)
    {
        var rot = _config.Rotation;
        if (!rot.Enabled || rot.Maps.Count == 0 || _changePending) return null;
        if (IsWarmup()) return null; // warmup "rounds" don't count toward rotation
        if (_finaleHoldActive) return null; // hold rotation — the finale round is still resolving

        _roundsOnMap++;
        if (_roundsOnMap < rot.RoundsPerMap) return null;

        var next = PickNext();
        if (next is null) return null;

        _changePending = true;
        plugin.AddTimer(Math.Max(3f, rot.ChangeDelaySeconds), () => Execute(next));
        _logger.LogInformation("[WC2] Rotating to {Map} in {Delay}s", next.Map, rot.ChangeDelaySeconds);
        return next.DisplayName ?? next.Map;
    }

    private static bool IsWarmup()
    {
        foreach (var proxy in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            return proxy.GameRules?.WarmupPeriod == true;
        return false;
    }

    /// <summary>Admin: force an immediate rotation to a named or next map.</summary>
    public bool ForceRotate(string? mapName)
    {
        RegionsFileConfig.RotationEntry? target = null;
        if (!string.IsNullOrEmpty(mapName))
        {
            foreach (var e in _config.Rotation.Maps)
                if (string.Equals(e.Map, mapName, StringComparison.OrdinalIgnoreCase)) { target = e; break; }
            // allow maps not in rotation: assume workshop when a collection is hosted
            target ??= new RegionsFileConfig.RotationEntry { Map = mapName, Workshop = true };
        }
        else target = PickNext();

        if (target is null) return false;
        Execute(target);
        return true;
    }

    private RegionsFileConfig.RotationEntry? PickNext()
    {
        var maps = _config.Rotation.Maps;
        var current = Server.MapName; // note: ID entries never match a live map name,
                                      // so ID-based rotations simply cycle in order.
        for (var i = 0; i < maps.Count; i++)
        {
            _cursor = (_cursor + 1) % maps.Count;
            if (!string.Equals(maps[_cursor].Map, current, StringComparison.OrdinalIgnoreCase))
                return maps[_cursor];
        }
        return null; // rotation only contains the current map
    }

    private void Execute(RegionsFileConfig.RotationEntry entry)
    {
        var map = entry.Map.Trim();
        if (map.StartsWith("ws:", StringComparison.OrdinalIgnoreCase)) map = map[3..].Trim();

        // Numeric → workshop ID (host_workshop_map handles any published map).
        // Name → ds_workshop_changelevel for hosted-collection maps, changelevel for stock.
        var cmd = ulong.TryParse(map, out _)
            ? $"host_workshop_map {map}"
            : entry.Workshop ? $"ds_workshop_changelevel {map}" : $"changelevel {map}";

        _logger.LogInformation("[WC2] Executing: {Cmd}", cmd);
        Server.ExecuteCommand(cmd);
    }
}
