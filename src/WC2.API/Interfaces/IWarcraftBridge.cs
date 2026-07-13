namespace WC2.API.Interfaces;

/// <summary>
/// Anti-corruption layer around NightFuryPrime/CS2-Warcraft-Plugin.
/// The framework NEVER references Warcraft types directly — only this bridge does,
/// via reflection against the loaded plugin assembly. When the submodule updates
/// and internals shift, only the bridge implementation changes.
/// </summary>
public interface IWarcraftBridge
{
    bool IsAvailable { get; }
    int  GetLevel(ulong steamId);
    string? GetRaceName(ulong steamId);
    /// <summary>Grants Warcraft XP through the core plugin so its own leveling pipeline fires.</summary>
    bool GrantXp(ulong steamId, int amount, string reason);
    float GetUltimateCooldownRemaining(ulong steamId);
}
