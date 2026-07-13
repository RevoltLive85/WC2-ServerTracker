using WC2.API.Models;

namespace WC2.API.Interfaces;

public interface IBossService
{
    /// <summary>Spawns a boss by definition id. Returns false if the id is unknown or a boss is already active.
    /// healthMultiplier scales max HP (finale rounds use large values).</summary>
    bool SpawnBoss(string bossId, string? reason = null, float healthMultiplier = 1f);
    bool DespawnActiveBoss(string reason);
    ActiveBossSnapshot? GetActiveBoss();
    IReadOnlyList<BossDefinition> GetDefinitions();
    void ReloadDefinitions();
}
