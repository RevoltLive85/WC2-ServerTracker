using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Interfaces;
using WC2.Events.Services;
using WC2.Events.WorldEvents;
using WC2.Shared;
using WC2.Shared.Configuration;
using WC2.Shared.Extensions;

namespace WC2.Events;

[MinimumApiVersion(200)]
public sealed class WC2EventsPlugin : BasePlugin
{
    public override string ModuleName => "WC2.Events";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "World events: Double XP, Treasure Goblin, invasions, seasonal.";

    private JsonConfigStore _store = null!;
    private WorldEventService _service = null!;

    public override void Load(bool hotReload)
    {
        _store = new JsonConfigStore(ModuleDirectory, Logger);
        var config = _store.LoadOrCreate("events.json", EventsFileConfig.Default);

        var bus = Wc2Bootstrap.EnsureCore(Logger);
        _service = new WorldEventService(config, bus, Logger);
        Capabilities.RegisterPluginCapability(Wc2Capabilities.Events, () => _service);

        AddTimer(1f, _service.Tick, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _service.MaybeStartRandomEvent();
            return HookResult.Continue;
        });

        // Treasure Goblin loot on kills while its event runs.
        RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            var a = ev.Attacker;
            if (a.IsRealPlayer() && a != ev.Userid)
                TreasureGoblinEvent.OnPlayerKill(a!.SteamID);
            return HookResult.Continue;
        });

        AddCommand("css_wc_event", "Start event: css_wc_event <id>", OnStartEvent);
        AddCommand("css_wc_event_stop", "Stop the active event", (_, cmd) =>
            cmd.ReplyToCommand(_service.StopActiveEvent("admin") ? "[WC2] Event stopped." : "[WC2] No active event."));
        AddCommand("css_wc_reload_events", "Reload events.json", (_, cmd) =>
        {
            _service.ApplyConfig(_store.LoadOrCreate("events.json", EventsFileConfig.Default));
            cmd.ReplyToCommand("[WC2] events.json reloaded.");
        });

        Logger.LogInformation("[WC2] Events module loaded with {Count} event definitions.", config.Events.Count);
    }

    [RequiresPermissions("@css/root")]
    private void OnStartEvent(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (cmd.ArgCount < 2) { cmd.ReplyToCommand("[WC2] Usage: css_wc_event <event_id>"); return; }
        cmd.ReplyToCommand(_service.StartEvent(cmd.GetArg(1), "admin")
            ? "[WC2] Event started." : "[WC2] Failed (unknown id or event already active).");
    }

}
