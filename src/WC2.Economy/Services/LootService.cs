using WC2.API.Models;

namespace WC2.Economy.Services;

/// <summary>Weighted loot rolls, data-driven from economy.json. Pure logic → unit-testable.</summary>
public sealed class LootService
{
    private Dictionary<string, LootTableDefinition> _tables = new(StringComparer.OrdinalIgnoreCase);

    public void ApplyTables(IEnumerable<LootTableDefinition> tables)
    {
        var map = new Dictionary<string, LootTableDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables) map[t.Id] = t;
        _tables = map;
    }

    public IReadOnlyList<LootDrop> Roll(string tableId, float luckMultiplier = 1f)
    {
        if (!_tables.TryGetValue(tableId, out var table) || table.Entries.Count == 0)
            return Array.Empty<LootDrop>();

        var rolls = Random.Shared.Next(table.MinRolls, table.MaxRolls + 1);
        var drops = new List<LootDrop>(rolls);

        var totalWeight = 0f;
        foreach (var e in table.Entries) totalWeight += e.Weight;

        for (var r = 0; r < rolls; r++)
        {
            var pick = Random.Shared.NextSingle() * totalWeight;
            foreach (var e in table.Entries)
            {
                pick -= e.Weight;
                if (pick > 0f) continue;

                CurrencyType? currency = Enum.TryParse<CurrencyType>(e.Currency, true, out var c) ? c : null;
                var amount = (long)(Random.Shared.NextInt64(e.MinAmount, e.MaxAmount + 1) * luckMultiplier);
                var rarity = Enum.TryParse<LootRarity>(e.Rarity, true, out var lr) ? lr : LootRarity.Common;
                drops.Add(new LootDrop(currency, Math.Max(1, amount), e.ItemId, e.DisplayName, rarity));
                break;
            }
        }
        return drops;
    }
}
