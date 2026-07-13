using WC2.API.Interfaces;
using WC2.API.Models;

namespace WC2.Economy.Services;

/// <summary>Thin composition of Wallet + Loot exposed as the module's single public capability.</summary>
public sealed class EconomyFacade : IEconomyService
{
    private readonly WalletService _wallets;
    private readonly LootService _loot;

    public EconomyFacade(WalletService wallets, LootService loot) { _wallets = wallets; _loot = loot; }

    public long GetBalance(ulong steamId, CurrencyType currency) => _wallets.GetBalance(steamId, currency);
    public void Grant(ulong steamId, CurrencyType currency, long amount, string source) => _wallets.Grant(steamId, currency, amount, source);
    public bool TrySpend(ulong steamId, CurrencyType currency, long amount, string sink) => _wallets.TrySpend(steamId, currency, amount, sink);
    public IReadOnlyList<LootDrop> RollLoot(string lootTableId, float luckMultiplier = 1f) => _loot.Roll(lootTableId, luckMultiplier);
}
