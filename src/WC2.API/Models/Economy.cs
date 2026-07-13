namespace WC2.API.Models;

public enum CurrencyType { Gold, BossToken, WorldstoneShard }

public sealed record LootDrop(CurrencyType? Currency, long Amount, string? ItemId, string DisplayName, LootRarity Rarity);

public enum LootRarity { Common, Uncommon, Rare, Epic, Legendary }

public sealed class LootTableDefinition
{
    public required string Id { get; init; }
    public List<LootEntry> Entries { get; init; } = new();
    public int MinRolls { get; init; } = 1;
    public int MaxRolls { get; init; } = 3;
}

public sealed class LootEntry
{
    public float Weight { get; init; } = 1f;
    public string? Currency { get; init; }      // parsed to CurrencyType
    public long MinAmount { get; init; }
    public long MaxAmount { get; init; }
    public string? ItemId { get; init; }
    public string DisplayName { get; init; } = "";
    public string Rarity { get; init; } = "Common";
}
