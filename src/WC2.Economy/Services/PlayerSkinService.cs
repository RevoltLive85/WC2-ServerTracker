using System.Text.Json;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace WC2.Economy.Services;

/// <summary>
/// Owned + equipped player skins, persisted per SteamID (write-behind, like wallets).
/// The equipped model is applied 0.5s after each spawn — late enough to win the
/// race against the Warcraft plugin's own class-model application.
/// </summary>
public sealed class PlayerSkinService
{
    private sealed class SkinState
    {
        public HashSet<string> Owned { get; set; } = new();
        public string? Equipped { get; set; }
    }

    private readonly string _dataDir;
    private readonly ILogger _logger;
    private readonly Dictionary<ulong, SkinState> _states = new(128);
    private readonly HashSet<ulong> _dirty = new();

    public PlayerSkinService(string moduleDirectory, ILogger logger)
    {
        _dataDir = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "wc2-data", "skins"));
        Directory.CreateDirectory(_dataDir);
        _logger = logger;
    }

    private SkinState GetState(ulong steamId)
    {
        if (_states.TryGetValue(steamId, out var s)) return s;
        var path = Path.Combine(_dataDir, steamId + ".json");
        try
        {
            s = File.Exists(path)
                ? JsonSerializer.Deserialize<SkinState>(File.ReadAllText(path)) ?? new SkinState()
                : new SkinState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WC2] Skin state load failed for {SteamId}", steamId);
            s = new SkinState();
        }
        _states[steamId] = s;
        return s;
    }

    public bool Owns(ulong steamId, string itemId) => GetState(steamId).Owned.Contains(itemId);
    public string? Equipped(ulong steamId) => GetState(steamId).Equipped;

    public void Grant(ulong steamId, string itemId)
    {
        GetState(steamId).Owned.Add(itemId);
        _dirty.Add(steamId);
    }

    public void Equip(ulong steamId, string? itemId)
    {
        var s = GetState(steamId);
        if (itemId is not null && !s.Owned.Contains(itemId)) return;
        s.Equipped = itemId;
        _dirty.Add(steamId);
    }

    /// <summary>Apply the equipped skin after spawn. Delay lets the Warcraft plugin
    /// set its class model first; ours lands on top.</summary>
    public void ApplyOnSpawn(CCSPlayerController player, BasePlugin plugin, Func<string, string?> resolveModel)
    {
        var equipped = Equipped(player.SteamID);
        if (equipped is null) return;
        var model = resolveModel(equipped);
        if (string.IsNullOrEmpty(model)) return;

        plugin.AddTimer(0.5f, () =>
        {
            if (player is { IsValid: true, PawnIsAlive: true } && player.PlayerPawn.Value is { } pawn)
                pawn.SetModel(model);
        });
    }

    public void Flush()
    {
        foreach (var steamId in _dirty)
            if (_states.TryGetValue(steamId, out var s))
                _ = File.WriteAllTextAsync(Path.Combine(_dataDir, steamId + ".json"), JsonSerializer.Serialize(s));
        _dirty.Clear();
    }
}
