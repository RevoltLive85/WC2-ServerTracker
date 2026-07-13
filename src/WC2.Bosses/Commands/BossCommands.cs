using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using WC2.API.Interfaces;

namespace WC2.Bosses.Commands;

public sealed class BossCommands
{
    private readonly IBossService _bosses;
    public BossCommands(IBossService bosses) => _bosses = bosses;

    public void Register(BasePlugin plugin)
    {
        plugin.AddCommand("css_wc_boss", "Spawn a boss: css_wc_boss <id>", OnSpawn);
        plugin.AddCommand("css_wc_boss_kill", "Force-despawn the active boss", OnDespawn);
        plugin.AddCommand("css_wc_bosses", "List boss definitions", OnList);
    }

    [RequiresPermissions("@css/root")]
    private void OnSpawn(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (cmd.ArgCount < 2) { cmd.ReplyToCommand("[WC2] Usage: css_wc_boss <boss_id>"); return; }
        var ok = _bosses.SpawnBoss(cmd.GetArg(1), reason: "admin");
        cmd.ReplyToCommand(ok ? "[WC2] Boss spawned." : "[WC2] Spawn failed (unknown id or boss active).");
    }

    [RequiresPermissions("@css/root")]
    private void OnDespawn(CCSPlayerController? caller, CommandInfo cmd) =>
        cmd.ReplyToCommand(_bosses.DespawnActiveBoss("admin")
            ? "[WC2] Boss despawned." : "[WC2] No active boss.");

    private void OnList(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var def in _bosses.GetDefinitions())
            cmd.ReplyToCommand($"[WC2] {def.Id} — {def.DisplayName}, {def.Title} (min {def.MinPlayers} players)");
    }
}
