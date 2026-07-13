using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.Shared;
using WC2.Shared.Configuration;
using WC2.World.Services;

namespace WC2.World;

[MinimumApiVersion(200)]
public sealed class WC2WorldPlugin : BasePlugin
{
    public override string ModuleName => "WC2.World";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "Regions, discovery banners, atmosphere, region bosses.";

    private JsonConfigStore _store = null!;
    private RegionService _regions = null!;
    private MapRotationService _rotation = null!;
    private IWc2EventBus _bus = null!;
    private bool _finaleActive;

    /// <summary>Single point of truth for ending a finale: boss killed, the finale's own
    /// duration clock running out, or the absolute backstop. v2 SIMPLIFIED DESIGN: the
    /// finale boss spawns through the exact same proven path as regular region bosses —
    /// no team merges, no bot kicks, no round-win-condition suspension. Every one of those
    /// engine-fighting tricks caused instability (premature round-ends, crashes at startup);
    /// this version trades the "everyone on one team" spectacle for rock-solid stability.
    /// The full team-merge finale can return as a v2 once it can be tested off-live.</summary>
    private void ResolveFinale(string reason, string chatMessage, float rotateDelay)
    {
        if (!_finaleActive) return;
        _finaleActive = false;
        _rotation.SetFinaleHold(false);
        Wc2Capabilities.Bosses.GetOrNull()?.DespawnActiveBoss($"finale_resolved_{reason}");
        Server.PrintToChatAll(chatMessage);
        var next = _rotation.RequestRotation(this, rotateDelay);
        if (next is not null)
            Wc2Capabilities.Hud.GetOrNull()?.Banner("REALM SHIFT", $"Traveling to {next}...", "#b58cff", 8f);
        Logger.LogInformation("[WC2] Finale resolved: {Reason}", reason);
    }

    public override void Load(bool hotReload)
    {
        _store = new JsonConfigStore(ModuleDirectory, Logger);
        var config = _store.LoadOrCreate("regions.json", RegionsFileConfig.Default);

        _bus = Wc2Bootstrap.EnsureCore(Logger);
        _regions = new RegionService(config);
        _rotation = new MapRotationService(config, Logger);
        Capabilities.RegisterPluginCapability(Wc2Capabilities.World, () => _regions);

        // ── BOSS FINALE (v2 simplified): last round of every map = a massively buffed boss ──
        // Spawns through the SAME proven path as regular region bosses. Teams stay as they
        // are; no engine-fighting. The boss joins the fight as a super-powered enemy and
        // everyone is incentivized to bring it down before the clock runs out.
        // ── Re-assert cvars that gamemode_*.cfg keeps resetting, every round (not just map start) ──
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.ExecuteCommand("mp_warmuptime 4");
            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            if (_finaleActive || !_rotation.IsFinaleRoundStarting()) return HookResult.Continue;
            _finaleActive = true;
            _rotation.SetFinaleHold(true);

            Server.PrintToChatAll(" \x02[WORLD]\x01 FINAL ROUND! The realm's champion descends — \x04bring it down before time runs out!\x01");
            Wc2Capabilities.Hud.GetOrNull()?.Banner("CHAMPION'S STAND",
                "The realm's champion has descended. Slay it for glory and loot!", "#ff4b3a", 8f);

            // Spawn the buffed boss via the standard, battle-tested boss path.
            AddTimer(2.0f, () =>
            {
                try
                {
                    var bosses = Wc2Capabilities.Bosses.GetOrNull();
                    if (bosses is null) return;
                    bosses.DespawnActiveBoss("finale_takeover"); // clear any regular boss first
                    var bossId = _rotation.FinaleForcedBossId ?? _regions.CurrentRegion?.RegionBossId ?? _rotation.FinaleFallbackBossId;
                    if (!bosses.SpawnBoss(bossId, "finale", _rotation.FinaleHealthMultiplier))
                        Logger.LogWarning("[WC2] Finale boss spawn failed for '{Id}'", bossId);
                }
                catch (Exception ex) { Logger.LogError(ex, "[WC2] Finale boss spawn threw"); }
            });

            // The finale's own clock: resolves on boss death or when this runs out.
            AddTimer(_rotation.FinaleDurationSeconds, () => ResolveFinale("time_up",
                " \x02[WORLD]\x01 DEFEAT! The champion withstands your assault — the realm falls to darkness this day...", 8f));

            // Absolute backstop in case the duration is ever misconfigured to something absurd.
            AddTimer(300f, () => ResolveFinale("absolute_timeout",
                " \x04[WORLD]\x01 The champion's stand drags on too long... the realm moves on regardless.", 8f));

            return HookResult.Continue;
        });

        // Boss killed mid-round → resolve immediately, don't wait for the timer.
        _bus.Subscribe<BossKilledEvent>(_ => ResolveFinale("boss_killed",
            " \x06[WORLD]\x01 THE CHAMPION FALLS! The realm shifts in your honor...", 10f));

        // ── Workshop-aware map rotation + finale round-end resolution ──
        // In bomb-defusal a round genuinely concludes on win OR loss, so round-end is the
        // correct signal to end the finale: if we DON'T resolve here, a lost finale round
        // just rolls into another normal round with the boss still possessed and active,
        // while the separate duration timer keeps counting — exactly the "bonus round then
        // map-switch 30s later" bug. The FinaleDurationSeconds timer below remains only as a
        // backstop for the rare case the boss outlives the entire round timer.
        // ── Round-end: resolve the finale (round concluded = fight over, win or lose) ──
        RegisterEventHandler<EventRoundEnd>((ev, _) =>
        {
            // CS2 fires EventRoundEnd for warmup expiry and game restarts too — with a
            // non-team "winner" (< 2). Counting those inflated the rounds-on-map counter,
            // which made the finale fire several rounds early (e.g. at round 3 of 6).
            // Only genuine rounds — won by T (2) or CT (3) — advance the map clock.
            if (ev.Winner < (int)CsTeam.Terrorist) return HookResult.Continue;

            if (_finaleActive)
            {
                ResolveFinale("round_over",
                    " \x02[WORLD]\x01 The round ends with the champion STILL STANDING — defeat! The realm shifts onward...", 8f);
                return HookResult.Continue;
            }

            var next = _rotation.OnRoundEnd(this);
            if (next is not null)
            {
                Server.PrintToChatAll($" \x10[WORLD]\x01 The realm shifts... next stop: \x04{next}\x01");
                Wc2Capabilities.Hud.GetOrNull()?.Banner("REALM SHIFT", $"Traveling to {next}...", "#b58cff", 8f);
            }
            return HookResult.Continue;
        });

        AddCommand("css_wc_nextmap", "Force rotation: css_wc_nextmap [map]", (caller, cmd) =>
        {
            var ok = _rotation.ForceRotate(cmd.ArgCount > 1 ? cmd.GetArg(1) : null);
            cmd.ReplyToCommand(ok ? "[WC2] Rotating..." : "[WC2] No rotation target available.");
        });

        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            // ── Re-assert cvars that gamemode_*.cfg silently resets after autoexec.cfg ──
            Server.ExecuteCommand("mp_autokick 0");
            Server.ExecuteCommand("mp_humanteam CT");
            Server.ExecuteCommand("mp_warmuptime 4");
            _rotation.OnMapStart();
            _finaleActive = false;
            _rotation.SetFinaleHold(false);

            // Defer one frame so all entities/players are settled before we announce.
            Server.NextFrame(() =>
            {
                var region = _regions.EnterMap(mapName);
                if (region is null)
                {
                    Logger.LogInformation("[WC2] Map {Map} belongs to no region.", mapName);
                    return;
                }
                _bus.Publish(new RegionEnteredEvent(region, mapName));

                // Region boss auto-spawn, delayed so the round has started. RETRIES every
                // 60s while blocked (e.g. an invasion boss is up when the timer first fires) —
                // previously a one-shot that silently never spawned if anything was active.
                if (region.RegionBossId is { } bossId)
                {
                    var attempts = 0;
                    CounterStrikeSharp.API.Modules.Timers.Timer? retry = null;
                    // STOP_ON_MAPCHANGE is critical: the server boots into de_dust2 then
                    // changelevels to the workshop map seconds later — without the flag,
                    // dust2's region-boss timer LEAKED into the next map and spawned the
                    // wrong region's boss there, blocking the correct one all map long.
                    AddTimer(90f, () =>
                    {
                        if (Wc2Capabilities.Bosses.GetOrNull()?.SpawnBoss(bossId, "region") == true) return;
                        retry = AddTimer(60f, () =>
                        {
                            if (++attempts > 5 || _finaleActive ||
                                Wc2Capabilities.Bosses.GetOrNull()?.SpawnBoss(bossId, "region") == true)
                                retry?.Kill();
                        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT | CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                    }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
                }
            });
        });

        // Ambient atmosphere lines — one cheap timer, no per-frame cost.
        AddTimer(config.AmbientLineIntervalSeconds, () =>
        {
            var region = _regions.CurrentRegion;
            if (region is null || region.AmbientLines.Count == 0) return;
            var line = region.AmbientLines[Random.Shared.Next(region.AmbientLines.Count)];
            Server.PrintToChatAll($" \x10[{region.DisplayName}]\x01 {line}");
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        AddCommand("css_wc_reload_regions", "Reload regions.json", (_, cmd) =>
        {
            var fresh = _store.LoadOrCreate("regions.json", RegionsFileConfig.Default);
            _regions.ApplyConfig(fresh);
            _rotation.ApplyConfig(fresh);
            cmd.ReplyToCommand("[WC2] regions.json reloaded.");
        });

        Logger.LogInformation("[WC2] World module loaded with {Count} regions.", config.Regions.Count);
    }

}
