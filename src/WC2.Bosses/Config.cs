using WC2.API.Models;

namespace WC2.Bosses;

public sealed class BossesFileConfig
{
    public float RespawnCooldownMinutes { get; set; } = 12f;
    /// <summary>Strip all guns from the boss avatar; melee + boss abilities only.</summary>
    public bool KnifeOnly { get; set; } = true;
    /// <summary>Boss model scale (1.0 = normal). Set 0 to never touch the skeleton scale
    /// (diagnostic: some custom models T-pose when scaled).</summary>
    public float ModelScale { get; set; } = 1.25f;
    public bool  AutoSpawnRegionBoss { get; set; } = true;
    public float AutoSpawnDelaySeconds { get; set; } = 90f;
    public List<BossDefinition> Bosses { get; set; } = new();

    public static BossesFileConfig Default() => new()
    {
        Bosses =
        {
            new BossDefinition
            {
                Id = "grommash_the_molten",
                DisplayName = "Grommash the Molten",
                Title = "Warlord of the Volcanic Realm",
                RegionId = "volcanic_realm",
                BaseHealth = 12_000, HealthPerPlayer = 1_800,
                DamageMultiplier = 1.4f, LootTableId = "boss_volcanic",
                MinPlayers = 4,
                SpawnLines = { "The ground splits. Magma breathes.", "Grommash has awakened beneath the caldera!" },
                DeathLines = { "The molten warlord crumbles to obsidian." },
                Phases =
                {
                    new BossPhaseDefinition { HealthThreshold = 1.0f, Name = "Smoldering", Abilities = { "flame_nova" }, AbilityIntervalSeconds = 14 },
                    new BossPhaseDefinition { HealthThreshold = 0.6f, Name = "Erupting",   Abilities = { "flame_nova", "magma_leap" }, AbilityIntervalSeconds = 10,
                        AnnounceHtml = "<font color='#ff6a3c'>Grommash ERUPTS! The floor is lava — keep moving!</font>" },
                    new BossPhaseDefinition { HealthThreshold = 0.25f, Name = "Meltdown",  Abilities = { "flame_nova", "magma_leap", "enrage" }, AbilityIntervalSeconds = 7,
                        AnnounceHtml = "<font color='#ff2e2e'>MELTDOWN — burn him down NOW!</font>" }
                }
            },
            new BossDefinition
            {
                Id = "sylvara_frostwhisper",
                DisplayName = "Sylvara Frostwhisper",
                Title = "Banshee of the Frozen North",
                RegionId = "frozen_north",
                BaseHealth = 9_000, HealthPerPlayer = 1_400,
                DamageMultiplier = 1.1f, LootTableId = "boss_frozen",
                MinPlayers = 3,
                SpawnLines = { "A chill silences the battlefield...", "Sylvara Frostwhisper drifts among you." },
                DeathLines = { "Her wail fades into falling snow." },
                Phases =
                {
                    new BossPhaseDefinition { HealthThreshold = 1.0f, Name = "Whisper", Abilities = { "frost_slow" }, AbilityIntervalSeconds = 12 },
                    new BossPhaseDefinition { HealthThreshold = 0.4f, Name = "Scream",  Abilities = { "frost_slow", "blizzard" }, AbilityIntervalSeconds = 8,
                        AnnounceHtml = "<font color='#9fdcff'>Sylvara SCREAMS — the blizzard descends!</font>" }
                }
            }
        }
    };
}
