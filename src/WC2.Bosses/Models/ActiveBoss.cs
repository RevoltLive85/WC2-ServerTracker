using CounterStrikeSharp.API.Core;
using WC2.API.Models;

namespace WC2.Bosses.Models;

/// <summary>Mutable live state of the current encounter. One at a time by design —
/// a boss should feel like a server-wide moment, not background noise.</summary>
public sealed class ActiveBoss
{
    public required BossDefinition Definition { get; init; }
    public required long MaxHealth { get; init; }
    public long CurrentHealth { get; set; }
    public int PhaseIndex { get; set; }
    /// <summary>Additive move-speed bonus from abilities (e.g. Enrage); re-asserted by ApplyBuffs.</summary>
    public float SpeedBonus { get; set; }
    /// <summary>Finale mode: avatar is forced onto the enemy team so ALL players+bots fight it together.</summary>
    public bool ForceEnemyTeam { get; init; }
    public DateTime SpawnedUtc { get; } = DateTime.UtcNow;
    public float NextAbilityTime { get; set; }

    /// <summary>The pawn we "possess". Preallocated dictionary keeps the hot damage path allocation-free.</summary>
    public CCSPlayerController? Avatar { get; set; }
    public Dictionary<ulong, long> DamageBySteamId { get; } = new(64);
    public Dictionary<ulong, float> Threat { get; } = new(64);

    public BossPhaseDefinition Phase => Definition.Phases[Math.Clamp(PhaseIndex, 0, Definition.Phases.Count - 1)];

    public ActiveBossSnapshot Snapshot()
    {
        ulong? top = null; float best = -1f;
        foreach (var kv in Threat)
            if (kv.Value > best) { best = kv.Value; top = kv.Key; }
        return new ActiveBossSnapshot(Definition.Id, Definition.DisplayName, Definition.Title,
            CurrentHealth, MaxHealth, PhaseIndex, Phase.Name, top, SpawnedUtc);
    }
}
