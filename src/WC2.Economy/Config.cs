using WC2.API.Models;

namespace WC2.Economy;

public sealed class EconomyFileConfig
{
    public long GoldPerKill { get; set; } = 6;
    public long GoldPerHeadshot { get; set; } = 10;
    public long GoldPerRoundWin { get; set; } = 25;
    public List<ShopItem> ShopItems { get; set; } = new();
    public List<LootTableDefinition> LootTables { get; set; } = new();

    public sealed class ShopItem
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public string Currency { get; init; } = "Gold";
        public long Price { get; init; }
        public string Description { get; init; } = "";
        /// <summary>If set, this item is a player skin: buying grants permanent
        /// ownership and equips it. Model must exist in a mounted addon.</summary>
        public string? ModelPath { get; init; }
    }

    public static EconomyFileConfig Default() => new()
    {
        ShopItems =
        {
            new ShopItem { Id = "title_bosshunter", DisplayName = "Title: Boss Hunter", Currency = "BossToken", Price = 5, Description = "Show off in chat." },
            new ShopItem { Id = "trail_ember",      DisplayName = "Ember Trail",        Currency = "WorldstoneShard", Price = 3, Description = "Cosmetic movement trail." },
            new ShopItem { Id = "xp_scroll_small",  DisplayName = "Scroll of Wisdom (+500 XP)", Currency = "Gold", Price = 400, Description = "Instant Warcraft XP." }
        },
        LootTables =
        {
            new LootTableDefinition
            {
                Id = "default_boss", MinRolls = 2, MaxRolls = 3,
                Entries =
                {
                    new LootEntry { Weight = 60, Currency = "Gold", MinAmount = 50, MaxAmount = 150, DisplayName = "Pouch of Gold", Rarity = "Common" },
                    new LootEntry { Weight = 30, Currency = "BossToken", MinAmount = 1, MaxAmount = 2, DisplayName = "Boss Token", Rarity = "Rare" },
                    new LootEntry { Weight = 10, Currency = "WorldstoneShard", MinAmount = 1, MaxAmount = 1, DisplayName = "Worldstone Shard", Rarity = "Legendary" }
                }
            },
            new LootTableDefinition
            {
                Id = "boss_volcanic", MinRolls = 2, MaxRolls = 4,
                Entries =
                {
                    new LootEntry { Weight = 55, Currency = "Gold", MinAmount = 80, MaxAmount = 220, DisplayName = "Molten Gold", Rarity = "Common" },
                    new LootEntry { Weight = 35, Currency = "BossToken", MinAmount = 1, MaxAmount = 3, DisplayName = "Obsidian Token", Rarity = "Epic" },
                    new LootEntry { Weight = 10, Currency = "WorldstoneShard", MinAmount = 1, MaxAmount = 2, DisplayName = "Worldstone Shard", Rarity = "Legendary" }
                }
            },
            new LootTableDefinition
            {
                Id = "boss_frozen", MinRolls = 2, MaxRolls = 3,
                Entries =
                {
                    new LootEntry { Weight = 60, Currency = "Gold", MinAmount = 60, MaxAmount = 180, DisplayName = "Frost-touched Gold", Rarity = "Common" },
                    new LootEntry { Weight = 30, Currency = "BossToken", MinAmount = 1, MaxAmount = 2, DisplayName = "Banshee Token", Rarity = "Rare" },
                    new LootEntry { Weight = 10, Currency = "WorldstoneShard", MinAmount = 1, MaxAmount = 1, DisplayName = "Worldstone Shard", Rarity = "Legendary" }
                }
            }
        }
    };
}
