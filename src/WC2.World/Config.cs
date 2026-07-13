using WC2.API.Models;

namespace WC2.World;

public sealed class RegionsFileConfig
{
    public float AmbientLineIntervalSeconds { get; set; } = 240f;
    public RotationConfig Rotation { get; set; } = new();
    public List<RegionDefinition> Regions { get; set; } = new();

    public sealed class RotationConfig
    {
        public bool Enabled { get; set; } = true;
        /// <summary>Rounds played on a map before rotating (warmup rounds are not counted).</summary>
        public int RoundsPerMap { get; set; } = 6;
        /// <summary>The LAST round of each map becomes a Boss Finale: everyone (players + bots)
        /// is merged onto one team against a massively buffed boss on the other.</summary>
        public bool FinaleEnabled { get; set; } = true;
        public float FinaleHealthMultiplier { get; set; } = 4f;
        /// <summary>Boss used when the current map's region has no RegionBossId.</summary>
        public string FinaleFallbackBossId { get; set; } = "gorehowl_the_warsong";
        /// <summary>If set, ALWAYS used for the finale boss regardless of region — beats
        /// both the region's own boss and FinaleFallbackBossId.</summary>
        public string? FinaleForcedBossId { get; set; } = null;
        /// <summary>Fixed fight duration in seconds, independent of CS2's own round length
        /// (TDM rounds can be far shorter than a real boss fight needs). Resolves on
        /// boss death or when this runs out — whichever comes first.</summary>
        public float FinaleDurationSeconds { get; set; } = 90f;
        /// <summary>Bot count to restore after a finale ends (match your server.cfg bot_quota).</summary>
        public int NormalBotQuota { get; set; } = 16;
        /// <summary>Seconds after the final round ends before the level change.</summary>
        public float ChangeDelaySeconds { get; set; } = 12f;
        public List<RotationEntry> Maps { get; set; } = new();
    }

    public sealed class RotationEntry
    {
        public required string Map { get; init; }
        /// <summary>Pretty name for announcements; falls back to Map.</summary>
        public string? DisplayName { get; init; }
        /// <summary>true = part of the hosted workshop collection (uses ds_workshop_changelevel),
        /// false = stock Valve map (uses changelevel).</summary>
        public bool Workshop { get; init; } = true;
    }

    public static RegionsFileConfig Default() => new()
    {
        Rotation = new RotationConfig
        {
            Enabled = true,
            RoundsPerMap = 6,
            Maps =
            {
                new RotationEntry { Map = "ws:3476293371", DisplayName = "Warsong Gulch", Workshop = true },
                new RotationEntry { Map = "ws:3152430710", DisplayName = "Mills",         Workshop = true },
                new RotationEntry { Map = "ws:3132854332", DisplayName = "Foroglio",      Workshop = true },
                new RotationEntry { Map = "ws:3075706807", DisplayName = "Biome",         Workshop = true },
                new RotationEntry { Map = "ws:3121217565", DisplayName = "Thera",         Workshop = true },
                new RotationEntry { Map = "ws:3261289969", DisplayName = "Jura",          Workshop = true },
                new RotationEntry { Map = "ws:3070290240", DisplayName = "Brewery",       Workshop = true },
                new RotationEntry { Map = "ws:3329258290", DisplayName = "Basalt",        Workshop = true },
                new RotationEntry { Map = "ws:3195399109", DisplayName = "Maginot",       Workshop = true },
                new RotationEntry { Map = "ws:3464094213", DisplayName = "Outferno",      Workshop = true },
            }
        },
        Regions =
        {
            new RegionDefinition
            {
                Id = "starter_kingdom", DisplayName = "The Starter Kingdom",
                Flavor = "Where every legend begins", ColorHex = "#ffd35c",
                Difficulty = 1, RecommendedPlayers = 4,
                Maps = { "de_dust2", "de_mirage" },
                AmbientLines = { "Merchants call out across the bazaar...", "Bells ring from the citadel." }
            },
            new RegionDefinition
            {
                Id = "ancient_forest", DisplayName = "The Ancient Forest",
                Flavor = "The trees remember", ColorHex = "#5cff8a",
                Difficulty = 2, RecommendedPlayers = 6, XpBonus = 0.10f,
                Maps = { "de_ancient", "de_aztec" },
                AmbientLines = { "Something moves between the roots.", "Old magic hums in the canopy." }
            },
            new RegionDefinition
            {
                Id = "frozen_north", DisplayName = "The Frozen North",
                Flavor = "Cold enough to silence gunfire", ColorHex = "#9fdcff",
                Difficulty = 3, RecommendedPlayers = 8, XpBonus = 0.20f, GoldBonus = 0.10f,
                RegionBossId = "sylvara_frostwhisper",
                Maps = { "de_train", "de_office" },
                AmbientLines = { "A banshee's wail rides the wind...", "Ice crackles beneath your boots." }
            },
            new RegionDefinition
            {
                Id = "volcanic_realm", DisplayName = "The Volcanic Realm",
                Flavor = "Even the shadows burn here", ColorHex = "#ff6a3c",
                Difficulty = 4, RecommendedPlayers = 10, XpBonus = 0.35f, GoldBonus = 0.20f,
                RegionBossId = "grommash_the_molten",
                Maps = { "de_inferno", "de_nuke" },
                AmbientLines = { "Ash falls like snow.", "The caldera groans beneath the map." }
            },
            new RegionDefinition
            {
                Id = "capital_city", DisplayName = "The Capital City",
                Flavor = "Neutral ground. Mostly.", ColorHex = "#e8e2d0",
                Difficulty = 1, RecommendedPlayers = 12,
                Maps = { "de_italy", "cs_italy" },
                AmbientLines = { "Guards eye your weapons warily." }
            }
        }
    };
}
