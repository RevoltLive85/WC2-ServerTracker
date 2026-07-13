using WC2.API.Models;

namespace WC2.API.Interfaces;

public interface IEconomyService
{
    long GetBalance(ulong steamId, CurrencyType currency);
    /// <summary>Adds (or removes, if negative) currency. Emits CurrencyChangedEvent. Never goes below zero.</summary>
    void Grant(ulong steamId, CurrencyType currency, long amount, string source);
    /// <summary>Atomic spend; returns false without side effects if funds are insufficient.</summary>
    bool TrySpend(ulong steamId, CurrencyType currency, long amount, string sink);
    IReadOnlyList<LootDrop> RollLoot(string lootTableId, float luckMultiplier = 1f);
}
