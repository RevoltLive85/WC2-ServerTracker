using System.Linq;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
#if HAS_CS2MENUMANAGER
using CS2MenuManager.API.Menu;
#endif
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Economy.Services;

namespace WC2.Economy.Commands;

/// <summary>
/// Vendor UI as a centered menu (CSSharp CenterHtmlMenu — same presentation family
/// as the Warcraft class picker). Commands are css_vendor / css_market on purpose:
/// css_shop belongs to the Warcraft plugin's own item shop and must not collide.
/// </summary>
public sealed class ShopCommands
{
    private readonly IEconomyService _economy;
    private readonly Func<EconomyFileConfig> _config;
    private readonly IWarcraftBridge? _warcraft;
    private readonly PlayerSkinService _skins;
    private BasePlugin _plugin = null!;

    public ShopCommands(IEconomyService economy, Func<EconomyFileConfig> config, IWarcraftBridge? warcraft, PlayerSkinService skins)
    { _economy = economy; _config = config; _warcraft = warcraft; _skins = skins; }

    public void Register(BasePlugin plugin)
    {
        _plugin = plugin;
        plugin.AddCommand("css_gold",   "Show your balances", OnBalance);
        plugin.AddCommand("css_vendor", "Open the vendor", OnVendor);
        plugin.AddCommand("css_market", "Open the vendor", OnVendor);
        plugin.AddCommand("css_buy",    "Buy item: css_buy <id>", OnBuyCommand);
        plugin.AddCommand("css_skins",  "Equip/unequip owned skins", OnSkins);
    }

    private void OnBalance(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is null) return;
        var id = player.SteamID;
        cmd.ReplyToCommand($"[WC2] Gold: {_economy.GetBalance(id, CurrencyType.Gold)}" +
                           $" | Boss Tokens: {_economy.GetBalance(id, CurrencyType.BossToken)}" +
                           $" | Worldstone Shards: {_economy.GetBalance(id, CurrencyType.WorldstoneShard)}");
    }

    /// <summary>Categorizes a shop item for the vendor's category menu. Inferred from the
    /// item id so no config schema change is needed. Keeps each sub-menu short enough that
    /// it never overflows the screen (the flat 22-item list did — WasdMenu has no page size).</summary>
    private static string CategoryOf(EconomyFileConfig.ShopItem item)
    {
        if (item.ModelPath is null) return "Consumables";
        var id = item.Id;
        if (id.Contains("doom")) return "Doom Slayer";
        if (id is "skin_itachi" or "skin_goku_v1" or "skin_goku_v2" or "skin_ravenpool") return "Anime";
        if (id is "skin_crysis1" or "skin_crysis3" or "skin_nanogirl") return "Heroes & Armored";
        if (id is "skin_cosmic" or "skin_frozon" or "skin_mezmer") return "Cosmic & Special";
        return "Meme Skins"; // ishowspeed, mrbeast, incredible, cj, sushiman, monkey, banana
    }

    private static readonly string[] CategoryOrder =
        { "Anime", "Heroes & Armored", "Doom Slayer", "Cosmic & Special", "Meme Skins", "Consumables" };

    private void OnVendor(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is null) return;
        OpenVendorCategories(player);
    }

    /// <summary>Top level: one entry per non-empty category.</summary>
    private void OpenVendorCategories(CCSPlayerController player)
    {
        var gold = _economy.GetBalance(player.SteamID, CurrencyType.Gold);
        var tokens = _economy.GetBalance(player.SteamID, CurrencyType.BossToken);
        var items = _config().ShopItems;

#if HAS_CS2MENUMANAGER
        var menu = new WasdMenu($"The Wandering Peddler  (Gold: {gold:N0} | Tokens: {tokens})", _plugin);
        foreach (var cat in CategoryOrder)
        {
            var c = cat;
            var count = items.Count(i => CategoryOf(i) == c);
            if (count == 0) continue;
            menu.AddItem($"{c}  ({count}) ▸", (p, _) => OpenVendorCategory(p, c));
        }
        menu.Display(player, 0);
#else
        var menu = new CenterHtmlMenu(
            $"<font color='#ffd35c'>The Wandering Peddler</font> <font color='#ffffff'>(⛃ {gold:N0} | 🎟 {tokens})</font>", _plugin);
        foreach (var cat in CategoryOrder)
        {
            var c = cat;
            var count = items.Count(i => CategoryOf(i) == c);
            if (count == 0) continue;
            menu.AddMenuOption($"{c} ({count})", (p, _) => OpenVendorCategory(p, c));
        }
        menu.AddMenuOption("Close", (p, _) => MenuManager.CloseActiveMenu(p));
        MenuManager.OpenCenterHtmlMenu(_plugin, player, menu);
#endif
    }

    /// <summary>Second level: the items within one category (short list, no overflow).</summary>
    private void OpenVendorCategory(CCSPlayerController player, string category)
    {
        var itemsInCat = _config().ShopItems.Where(i => CategoryOf(i) == category).ToList();

#if HAS_CS2MENUMANAGER
        var menu = new WasdMenu($"Peddler ▸ {category}", _plugin);
        foreach (var item in itemsInCat)
        {
            var captured = item;
            var owned = captured.ModelPath is not null && _skins.Owns(player.SteamID, captured.Id);
            menu.AddItem(owned ? $"{captured.DisplayName} — Owned (equip)"
                               : $"{captured.DisplayName} — {captured.Price} {captured.Currency}",
                (p, _) => TryBuy(p, captured));
        }
        menu.AddItem("◂ Back", (p, _) => OpenVendorCategories(p));
        menu.Display(player, 0);
#else
        var menu = new CenterHtmlMenu($"<font color='#ffd35c'>Peddler ▸ {category}</font>", _plugin);
        foreach (var item in itemsInCat)
        {
            var captured = item;
            var owned = captured.ModelPath is not null && _skins.Owns(player.SteamID, captured.Id);
            menu.AddMenuOption(owned ? $"{captured.DisplayName} — Owned (equip)"
                                     : $"{captured.DisplayName} — {captured.Price} {captured.Currency}",
                (p, _) => TryBuy(p, captured));
        }
        menu.AddMenuOption("◂ Back", (p, _) => OpenVendorCategories(p));
        MenuManager.OpenCenterHtmlMenu(_plugin, player, menu);
#endif
    }

    private void OnBuyCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is null) return;
        if (cmd.ArgCount < 2) { cmd.ReplyToCommand("[WC2] Usage: css_buy <item_id> (or just use css_vendor)"); return; }

        foreach (var i in _config().ShopItems)
            if (string.Equals(i.Id, cmd.GetArg(1), StringComparison.OrdinalIgnoreCase))
            { TryBuy(player, i); return; }
        cmd.ReplyToCommand("[WC2] Unknown item.");
    }

    private void TryBuy(CCSPlayerController player, EconomyFileConfig.ShopItem item)
    {
        // Skins: owning already means equip, not re-buy.
        if (item.ModelPath is not null && _skins.Owns(player.SteamID, item.Id))
        {
            _skins.Equip(player.SteamID, item.Id);
            player.PrintToChat($" [WC2] Equipped {item.DisplayName}. Applies on your next spawn.");
            return;
        }

        if (!Enum.TryParse<CurrencyType>(item.Currency, true, out var currency))
        { player.PrintToChat(" [WC2] Item misconfigured (currency)."); return; }

        if (!_economy.TrySpend(player.SteamID, currency, item.Price, $"shop:{item.Id}"))
        { player.PrintToChat($" [WC2] Not enough {item.Currency}."); return; }

        if (item.ModelPath is not null)
        {
            _skins.Grant(player.SteamID, item.Id);
            _skins.Equip(player.SteamID, item.Id);
            player.PrintToChat($" [WC2] Purchased & equipped {item.DisplayName}! Applies on your next spawn.");
        }
        else if (item.Id == "xp_scroll_small" && _warcraft?.GrantXp(player.SteamID, 500, "shop") == true)
            player.PrintToChat(" [WC2] You feel wiser. (+500 XP)");
        else if (item.Id == "worldstone_xp_scroll" && _warcraft?.GrantXp(player.SteamID, 2500, "shop_shard") == true)
            player.PrintToChat(" \x04[WC2]\x01 The Worldstone's power surges through you! (+2500 XP)");
        else if (item.Id == "shard_exchange_tokens")
        {
            _economy.Grant(player.SteamID, CurrencyType.BossToken, 8, "shop:shard_exchange");
            player.PrintToChat(" \x04[WC2]\x01 The Worldstone Shard dissolves into 8 Boss Tokens.");
        }
        else
            player.PrintToChat($" [WC2] Purchased {item.DisplayName}!");
    }

    private void OnSkins(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is null) return;
        var ownedItems = new List<EconomyFileConfig.ShopItem>();
        foreach (var i in _config().ShopItems)
            if (i.ModelPath is not null && _skins.Owns(player.SteamID, i.Id)) ownedItems.Add(i);
        if (ownedItems.Count == 0) { cmd.ReplyToCommand("[WC2] You own no skins yet — visit css_vendor!"); return; }

        var equipped = _skins.Equipped(player.SteamID);
#if HAS_CS2MENUMANAGER
        var menu = new WasdMenu("Your Skins", _plugin);
        menu.AddItem("Default appearance (unequip)", (p, _) => Unequip(p));
        foreach (var i in ownedItems)
        {
            var c = i;
            menu.AddItem(c.Id == equipped ? $"{c.DisplayName} [EQUIPPED]" : c.DisplayName, (p, _) => EquipSkin(p, c));
        }
        menu.AddItem("Close", (p, _) => MenuManager.CloseActiveMenu(p));
        menu.Display(player, 0);
#else
        var menu = new CenterHtmlMenu("Your Skins", _plugin);
        menu.AddMenuOption("Default appearance (unequip)", (p, _) => Unequip(p));
        foreach (var i in ownedItems)
        {
            var c = i;
            menu.AddMenuOption(c.Id == equipped ? $"{c.DisplayName} [EQUIPPED]" : c.DisplayName, (p, _) => EquipSkin(p, c));
        }
        menu.AddMenuOption("Close", (p, _) => MenuManager.CloseActiveMenu(p));
        MenuManager.OpenCenterHtmlMenu(_plugin, player, menu);
#endif
    }

    private void EquipSkin(CCSPlayerController p, EconomyFileConfig.ShopItem item)
    {
        _skins.Equip(p.SteamID, item.Id);
        p.PrintToChat($" [WC2] Equipped {item.DisplayName}. Applies on your next spawn.");
    }

    private void Unequip(CCSPlayerController p)
    {
        _skins.Equip(p.SteamID, null);
        p.PrintToChat(" [WC2] Skin unequipped — default appearance on next spawn.");
    }
}
