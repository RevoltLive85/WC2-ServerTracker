using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Economy.Commands;
using WC2.Economy.Services;
using WC2.Shared;
using WC2.Shared.Configuration;
using WC2.Shared.Extensions;

namespace WC2.Economy;

[MinimumApiVersion(200)]
public sealed class WC2EconomyPlugin : BasePlugin
{
    public override string ModuleName => "WC2.Economy";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "Gold, tokens, vendors, weighted loot tables.";

    private JsonConfigStore _store = null!;
    private EconomyFileConfig _config = null!;
    private WalletService _wallets = null!;
    private LootService _loot = null!;
    private PlayerSkinService _skins = null!;
    private IWc2EventBus _bus = null!;

    public override void Load(bool hotReload)
    {
        _store = new JsonConfigStore(ModuleDirectory, Logger);
        _config = _store.LoadOrCreate("economy.json", EconomyFileConfig.Default);

        _bus = Wc2Bootstrap.EnsureCore(Logger);
        _wallets = new WalletService(ModuleDirectory, _bus, Logger);
        _loot = new LootService();
        _loot.ApplyTables(_config.LootTables);

        var facade = new EconomyFacade(_wallets, _loot);
        Capabilities.RegisterPluginCapability(Wc2Capabilities.Economy, () => facade);

        _skins = new PlayerSkinService(ModuleDirectory, Logger);
        new ShopCommands(facade, () => _config, Wc2Capabilities.Warcraft.GetOrNull(), _skins).Register(this);

        // Skin models must be precached at map start.
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            foreach (var item in _config.ShopItems)
                if (!string.IsNullOrEmpty(item.ModelPath))
                    manifest.AddResource(item.ModelPath);
        });

        // Apply equipped skin shortly after each spawn (wins the race vs class models).
        RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
        {
            var p = ev.Userid;
            if (p.IsRealPlayer())
                _skins.ApplyOnSpawn(p!, this, id =>
                {
                    foreach (var item in _config.ShopItems)
                        if (item.Id == id) return item.ModelPath;
                    return null;
                });
            return HookResult.Continue;
        });
        AddCommand("css_wc_reload_economy", "Reload economy.json", (_, cmd) =>
        {
            _config = _store.LoadOrCreate("economy.json", EconomyFileConfig.Default);
            _loot.ApplyTables(_config.LootTables);
            cmd.ReplyToCommand("[WC2] economy.json reloaded.");
        });

        // ── Income hooks ────────────────────────────────────────
        RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            var attacker = ev.Attacker;
            if (!attacker.IsRealPlayer() || attacker == ev.Userid) return HookResult.Continue;
            var mult = Wc2Capabilities.Events.GetOrNull()?.CurrentGoldMultiplier ?? 1f;
            var gold = (long)((ev.Headshot ? _config.GoldPerHeadshot : _config.GoldPerKill) * mult);
            _wallets.Grant(attacker!.SteamID, CurrencyType.Gold, gold, ev.Headshot ? "headshot" : "kill");
            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundEnd>((ev, _) =>
        {
            var mult = Wc2Capabilities.Events.GetOrNull()?.CurrentGoldMultiplier ?? 1f;
            foreach (var p in PlayerExtensions.RealPlayers())
                if (p.TeamNum == ev.Winner)
                    _wallets.Grant(p.SteamID, CurrencyType.Gold, (long)(_config.GoldPerRoundWin * mult), "round_win");
            _wallets.Flush();
            return HookResult.Continue;
        });

        // ── Boss loot distribution: everyone on the damage sheet rolls ──
        _bus.Subscribe<BossKilledEvent>(OnBossKilled);

        AddTimer(30f, () => { _wallets.Flush(); _skins.Flush(); },
            CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        Logger.LogInformation("[WC2] Economy loaded: {Items} shop items, {Tables} loot tables.",
            _config.ShopItems.Count, _config.LootTables.Count);
    }

    private void OnBossKilled(BossKilledEvent evt)
    {
        long topDamage = 0;
        foreach (var v in evt.DamageBySteamId.Values) if (v > topDamage) topDamage = v;

        foreach (var (steamId, damage) in evt.DamageBySteamId)
        {
            // Contribution-scaled luck: top damage rolls at full luck, minimum 0.5.
            var luck = topDamage > 0 ? 0.5f + 0.5f * damage / (float)topDamage : 1f;
            var table = FindLootTable(evt.Boss.BossId);
            foreach (var drop in _loot.Roll(table, luck))
            {
                if (drop.Currency is { } c) _wallets.Grant(steamId, c, drop.Amount, $"boss:{evt.Boss.BossId}");
                _bus.Publish(new LootAwardedEvent(steamId, drop, $"boss:{evt.Boss.BossId}"));
            }
        }
        _wallets.Flush();
    }

    private string FindLootTable(string bossId)
    {
        var bosses = Wc2Capabilities.Bosses.GetOrNull();
        if (bosses is not null)
            foreach (var d in bosses.GetDefinitions())
                if (d.Id == bossId) return d.LootTableId;
        return "default_boss";
    }

    public override void Unload(bool hotReload) { _wallets.Flush(); _skins.Flush(); }

}
