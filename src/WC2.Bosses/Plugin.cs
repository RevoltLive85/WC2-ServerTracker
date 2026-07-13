using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WC2.API.Events;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Interfaces;
using WC2.Bosses.Commands;
using WC2.Bosses.Services;
using WC2.Shared;
using WC2.Shared.Configuration;

namespace WC2.Bosses;

[MinimumApiVersion(200)]
public sealed class WC2BossesPlugin : BasePlugin
{
    public override string ModuleName => "WC2.Bosses";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "Boss encounters: avatars, phases, threat, health scaling, loot events.";

    private JsonConfigStore _configStore = null!;
    private BossesFileConfig _config = null!;
    private BossManager _manager = null!;
    private BossAvatarService _avatars = null!;
    private IWc2EventBus _bus = null!;

    public override void Load(bool hotReload)
    {
        _configStore = new JsonConfigStore(ModuleDirectory, Logger);
        var config = _configStore.LoadOrCreate("bosses.json", BossesFileConfig.Default);
        _config = config;

        _bus = Wc2Bootstrap.EnsureCore(Logger);
        _avatars = new BossAvatarService(config, Logger) { Plugin = this };
        _manager = new BossManager(_bus, new BossAbilityRegistry(this), _avatars, config, Logger);
        Capabilities.RegisterPluginCapability(Wc2Capabilities.Bosses, () => _manager);

        new BossCommands(_manager).Register(this);
        AddCommand("css_wc_reload_bosses", "Reload bosses.json", (_, cmd) =>
        {
            var fresh = _configStore.LoadOrCreate("bosses.json", BossesFileConfig.Default);
            _config = fresh;
            _manager.ApplyConfig(fresh);
            _avatars.ApplyConfig(fresh);
            cmd.ReplyToCommand("[WC2] bosses.json reloaded.");
        });

        // ── Damage: anything hitting the tagged avatar drains encounter HP ──
        RegisterEventHandler<EventPlayerHurt>((ev, _) =>
        {
            if (_avatars.IsAvatar(ev.Userid))
                _manager.OnAvatarDamaged(ev.Attacker, ev.DmgHealth);
            return HookResult.Continue;
        });

        // ── Presence: claim freshly spawned bots / re-buff our avatar ──
        RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
        {
            var p = ev.Userid;
            if (p is { IsValid: true, IsBot: true })
                Server.NextFrame(() => { if (p.IsValid) _avatars.OnBotSpawned(p); });
            return HookResult.Continue;
        });

        // ── Avatar pawn death ≠ encounter death: blink & respawn while HP remains ──
        RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            var victim = ev.Userid;
            if (victim is not null && _avatars.IsAvatar(victim) && _manager.GetActiveBoss() is not null)
                _avatars.OnAvatarPawnDeath(victim, this);
            return HookResult.Continue;
        });

        // ── Map change: never leak a possessed bot across maps ──
        RegisterListener<Listeners.OnMapEnd>(() => _manager.DespawnActiveBoss("map_end"));

        // ── Workshop/custom boss models must be precached before SetModel works ──
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            foreach (var def in _manager.GetDefinitions())
                if (!string.IsNullOrEmpty(def.Model))
                    manifest.AddResource(def.Model);
        });

        // Diagnostic: apply a model to the CALLING PLAYER (no scale, no reassert).
        // If a human T-poses with the same model, the issue is the model/addon,
        // not our bot handling. Usage: css_wc_testmodel characters/models/...vmdl
        AddCommand("css_wc_testmodel", "Apply a model to yourself (diagnostic)", (player, cmd) =>
        {
            if (player is null || cmd.ArgCount < 2) { cmd.ReplyToCommand("[WC2] Usage: css_wc_testmodel <path.vmdl>"); return; }
            var path = cmd.GetArg(1);
            var pawn = player.PlayerPawn.Value;
            if (pawn is null) { cmd.ReplyToCommand("[WC2] No pawn (are you alive?)"); return; }
            AddTimer(0.3f, () => { if (player.IsValid && player.PlayerPawn.Value is { } p2) p2.SetModel(path); });
            cmd.ReplyToCommand($"[WC2] Applying {path} in 0.3s — check your model/animations (thirdperson or ask someone to look).");
        });

        // Cheap half-second encounter tick for abilities.
        AddTimer(0.5f, () => _manager.Tick(Server.CurrentTime),
            CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        Logger.LogInformation("[WC2] Bosses module loaded with {Count} definitions.", config.Bosses.Count);
    }

    public override void Unload(bool hotReload) => _manager.DespawnActiveBoss("plugin_unload");

}
