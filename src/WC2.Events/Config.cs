namespace WC2.Events;

public sealed class EventsFileConfig
{
    public float RandomEventChancePerRound { get; set; } = 0.08f;
    public List<WorldEventDefinition> Events { get; set; } = new();

    public sealed class WorldEventDefinition
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public string Type { get; init; } = "";           // maps to a registered handler class
        public float DurationSeconds { get; init; } = 180f;
        public float XpMultiplier { get; init; } = 1f;
        public float GoldMultiplier { get; init; } = 1f;
        public bool Random { get; init; } = true;          // eligible for random rotation
        public Dictionary<string, string> Parameters { get; init; } = new();
    }

    public static EventsFileConfig Default() => new()
    {
        Events =
        {
            new WorldEventDefinition { Id = "double_xp", DisplayName = "Double XP Hour", Type = "multiplier",
                DurationSeconds = 3600, XpMultiplier = 2f, Random = false },
            new WorldEventDefinition { Id = "gold_rush", DisplayName = "Gold Rush", Type = "multiplier",
                DurationSeconds = 300, GoldMultiplier = 2f },
            new WorldEventDefinition { Id = "treasure_goblin", DisplayName = "Treasure Goblin Sighted!", Type = "treasure_goblin",
                DurationSeconds = 120, Parameters = { ["loot_table"] = "default_boss" } },
            new WorldEventDefinition { Id = "invasion", DisplayName = "World Invasion", Type = "invasion",
                DurationSeconds = 240, XpMultiplier = 1.5f, GoldMultiplier = 1.5f,
                Parameters = { ["boss_pool"] = "grommash_the_molten,sylvara_frostwhisper" } }
        }
    };
}
