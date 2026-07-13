namespace WC2.API.Models;

public sealed class BossDefinition
{
    public required string Id { get; init; }               // "grommash_the_molten"
    public required string DisplayName { get; init; }      // "Grommash the Molten"
    public string Title { get; init; } = "";               // "Warlord of the Volcanic Realm"
    public string? RegionId { get; init; }                 // restrict spawns to a region, null = anywhere
    public string Model { get; init; } = "";               // optional custom model path
    public long BaseHealth { get; init; } = 10_000;
    public long HealthPerPlayer { get; init; } = 1_500;    // linear scaling with live players
    public float DamageMultiplier { get; init; } = 1.0f;
    public float MoveSpeed { get; init; } = 1.0f;
    public string LootTableId { get; init; } = "default_boss";
    public int MinPlayers { get; init; } = 4;
    public List<BossPhaseDefinition> Phases { get; init; } = new();
    public List<string> SpawnLines { get; init; } = new(); // flavor chat lines on spawn
    public List<string> DeathLines { get; init; } = new();
}

public sealed class BossPhaseDefinition
{
    /// <summary>Phase activates when health fraction drops to or below this value (1.0 → 0.0).</summary>
    public float HealthThreshold { get; init; } = 1.0f;
    public string Name { get; init; } = "Phase";
    public List<string> Abilities { get; init; } = new();  // ability ids resolved by BossAbilityRegistry
    public float AbilityIntervalSeconds { get; init; } = 12f;
    public string? AnnounceHtml { get; init; }
}

/// <summary>Immutable view of the live boss handed to other modules (UI, quests).</summary>
public sealed record ActiveBossSnapshot(
    string BossId, string DisplayName, string Title,
    long CurrentHealth, long MaxHealth, int PhaseIndex, string PhaseName,
    ulong? TopThreatSteamId, DateTime SpawnedUtc);
