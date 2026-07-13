using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Utils;
using WC2.API.Capabilities;
using WC2.API.Models;
using WC2.Shared.Extensions;
#if HAS_CS2MENUMANAGER
using CS2MenuManager.API.Menu;
#endif

namespace WC2.Admin.Menus;

/// <summary>
/// One-stop in-game admin console (!wc_admin / !admin), built on CS2MenuManager's
/// WASD menu. Every entry mirrors a command we found ourselves typing constantly:
/// boss spawn/despawn, event start/stop, map jumps, player grants, config reloads.
/// All actions re-check @css/root at execution time, not just at menu open.
/// </summary>
public sealed class AdminMenu
{
    private readonly BasePlugin _plugin;
    public AdminMenu(BasePlugin plugin) => _plugin = plugin;

    private static bool IsRoot(CCSPlayerController p) =>
        AdminManager.PlayerHasPermissions(p, "@css/root");

#if HAS_CS2MENUMANAGER

    // ── Main ───────────────────────────────────────────────────

    public void OpenMain(CCSPlayerController admin)
    {
        if (!IsRoot(admin)) { admin.PrintToChat(" [WC2] Admins only."); return; }

        var menu = new WasdMenu("WC2 Admin", _plugin);
        var boss = Wc2Capabilities.Bosses.GetOrNull()?.GetActiveBoss();
        var evt  = Wc2Capabilities.Events.GetOrNull()?.ActiveEventId;

        menu.AddItem("Bosses ▸", (p, _) => OpenBosses(p));
        menu.AddItem("Events ▸", (p, _) => OpenEvents(p));
        menu.AddItem("Change Map ▸", (p, _) => OpenMaps(p));
        menu.AddItem("Players ▸", (p, _) => OpenPlayers(p));
        menu.AddItem("Reload All Configs", (p, _) => ReloadAll(p));
        if (boss is not null)
            menu.AddItem($"⚔ Despawn: {boss.DisplayName}", (p, _) => Do(p,
                () => Wc2Capabilities.Bosses.GetOrNull()?.DespawnActiveBoss("admin_menu"), $"Despawned {boss.DisplayName}"));
        if (evt is not null)
            menu.AddItem($"⏹ Stop event: {evt}", (p, _) => Do(p,
                () => Wc2Capabilities.Events.GetOrNull()?.StopActiveEvent("admin_menu"), $"Stopped {evt}"));

        menu.Display(admin, 0);
    }

    // ── Bosses ─────────────────────────────────────────────────

    private void OpenBosses(CCSPlayerController admin)
    {
        var bosses = Wc2Capabilities.Bosses.GetOrNull();
        var menu = new WasdMenu("Admin ▸ Bosses", _plugin);
        if (bosses is null) menu.AddItem("Bosses module offline", (_, _) => { });
        else
        {
            var active = bosses.GetActiveBoss();
            if (active is not null)
                menu.AddItem($"⚔ Despawn {active.DisplayName}", (p, _) => Do(p,
                    () => bosses.DespawnActiveBoss("admin_menu"), $"Despawned {active.DisplayName}"));
            foreach (var def in bosses.GetDefinitions())
            {
                var d = def;
                menu.AddItem($"Spawn: {d.DisplayName}", (p, _) => Do(p,
                    () => bosses.SpawnBoss(d.Id, "admin"), $"Spawning {d.DisplayName}"));
            }
        }
        menu.AddItem("◂ Back", (p, _) => OpenMain(p));
        menu.Display(admin, 0);
    }

    // ── Events ─────────────────────────────────────────────────

    private void OpenEvents(CCSPlayerController admin)
    {
        var events = Wc2Capabilities.Events.GetOrNull();
        var menu = new WasdMenu("Admin ▸ Events", _plugin);
        if (events is null) menu.AddItem("Events module offline", (_, _) => { });
        else
        {
            if (events.ActiveEventId is { } active)
                menu.AddItem($"⏹ Stop: {active}", (p, _) => Do(p,
                    () => events.StopActiveEvent("admin_menu"), $"Stopped {active}"));
            foreach (var id in events.GetEventIds())
            {
                var e = id;
                menu.AddItem($"Start: {e}", (p, _) => Do(p,
                    () => events.StartEvent(e, "admin_menu"), $"Started {e}"));
            }
        }
        menu.AddItem("◂ Back", (p, _) => OpenMain(p));
        menu.Display(admin, 0);
    }

    // ── Maps (reads rotation from regions.json, decoupled from WC2.World) ──

    private sealed class RotationDto
    {
        public RotationBlock? Rotation { get; set; }
        public sealed class RotationBlock { public List<Entry> Maps { get; set; } = new(); }
        public sealed class Entry { public string Map { get; set; } = ""; public string? DisplayName { get; set; } public bool Workshop { get; set; } = true; }
    }

    private void OpenMaps(CCSPlayerController admin)
    {
        var menu = new WasdMenu("Admin ▸ Change Map", _plugin);
        menu.AddItem("Rotate to next map", (p, _) => Do(p,
            () => Server.ExecuteCommand("css_wc_nextmap"), "Rotating to next map"));
        try
        {
            var path = Path.GetFullPath(Path.Combine(_plugin.ModuleDirectory, "..", "..", "wc2-configs", "regions.json"));
            var dto = JsonSerializer.Deserialize<RotationDto>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var entry in dto?.Rotation?.Maps ?? new())
            {
                var e = entry;
                menu.AddItem($"Go: {e.DisplayName ?? e.Map}", (p, _) => Do(p, () =>
                {
                    var map = e.Map.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ? e.Map[3..].Trim() : e.Map.Trim();
                    var cmd = ulong.TryParse(map, out var _wsid) ? $"host_workshop_map {map}"
                            : e.Workshop ? $"ds_workshop_changelevel {map}" : $"changelevel {map}";
                    Server.PrintToChatAll($" \x10[WORLD]\x01 An admin shifts the realm to \x04{e.DisplayName ?? e.Map}\x01...");
                    _plugin.AddTimer(3f, () => Server.ExecuteCommand(cmd));
                }, $"Changing to {e.DisplayName ?? e.Map} in 3s"));
            }
        }
        catch { menu.AddItem("(rotation list unavailable)", (_, _) => { }); }
        menu.AddItem("◂ Back", (p, _) => OpenMain(p));
        menu.Display(admin, 0);
    }

    // ── Players ────────────────────────────────────────────────

    private void OpenPlayers(CCSPlayerController admin)
    {
        var menu = new WasdMenu("Admin ▸ Players", _plugin);
        foreach (var target in PlayerExtensions.RealPlayers())
        {
            var t = target;
            menu.AddItem(t.PlayerName, (p, _) => OpenPlayerActions(p, t));
        }
        menu.AddItem("◂ Back", (p, _) => OpenMain(p));
        menu.Display(admin, 0);
    }

    private void OpenPlayerActions(CCSPlayerController admin, CCSPlayerController target)
    {
        if (target is not { IsValid: true }) { OpenPlayers(admin); return; }
        var menu = new WasdMenu($"Admin ▸ {target.PlayerName}", _plugin);
        var eco = Wc2Capabilities.Economy.GetOrNull();
        var wc  = Wc2Capabilities.Warcraft.GetOrNull();

        menu.AddItem("Give 1,000 Gold", (p, _) => Do(p,
            () => eco?.Grant(target.SteamID, CurrencyType.Gold, 1000, "admin_menu"), $"+1,000 gold → {target.PlayerName}"));
        menu.AddItem("Give 10 Boss Tokens", (p, _) => Do(p,
            () => eco?.Grant(target.SteamID, CurrencyType.BossToken, 10, "admin_menu"), $"+10 tokens → {target.PlayerName}"));
        menu.AddItem("Give 5 Worldstone Shards", (p, _) => Do(p,
            () => eco?.Grant(target.SteamID, CurrencyType.WorldstoneShard, 5, "admin_menu"), $"+5 shards → {target.PlayerName}"));
        menu.AddItem("Give 500 Warcraft XP", (p, _) => Do(p,
            () => wc?.GrantXp(target.SteamID, 500, "admin_menu"), $"+500 XP → {target.PlayerName}"));
        menu.AddItem("Slay", (p, _) => Do(p,
            () => { if (target is { IsValid: true } && target.PlayerPawn.Value is { } pawn) pawn.CommitSuicide(false, true); },
            $"Slayed {target.PlayerName}"));
        menu.AddItem("Kick", (p, _) => Do(p,
            () => Server.ExecuteCommand($"kickid {target.UserId}"), $"Kicked {target.PlayerName}"));
        menu.AddItem("◂ Back", (p, _) => OpenPlayers(p));
        menu.Display(admin, 0);
    }

    // ── Reloads ────────────────────────────────────────────────

    private void ReloadAll(CCSPlayerController admin) => Do(admin, () =>
    {
        foreach (var cmd in new[] { "css_wc_reload_bosses", "css_wc_reload_economy",
                                    "css_wc_reload_regions", "css_wc_reload_quests", "css_wc_reload_events" })
            Server.ExecuteCommand(cmd);
    }, "All WC2 configs reloaded");

    // ── shared action wrapper: permission re-check + feedback ──

    private static void Do(CCSPlayerController admin, Action action, string feedback)
    {
        if (!IsRoot(admin)) { admin.PrintToChat(" [WC2] Admins only."); return; }
        try { action(); admin.PrintToChat($" \x04[WC2 Admin]\x01 {feedback}"); }
        catch (Exception ex) { admin.PrintToChat($" \x02[WC2 Admin]\x01 Failed: {ex.Message}"); }
    }

#else
    public void OpenMain(CCSPlayerController admin) =>
        admin.PrintToChat(" [WC2] Admin menu requires CS2MenuManager.dll in libs/ at build time. Falling back: use css_wc_* commands.");
#endif
}
