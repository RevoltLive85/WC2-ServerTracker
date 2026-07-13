using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using WC2.Bosses.Models;
using WC2.Shared.Extensions;

namespace WC2.Bosses.Services;

/// <summary>
/// Gives the boss a body. Strategy: commandeer a dedicated bot instead of building
/// custom NPC AI — the engine gives us pathfinding, target selection and aiming for
/// free, and it works on every map with a nav mesh. The bot is renamed, tagged
/// (Clan = WC2_BOSS, which BossManager's damage hook keys on), model-swapped,
/// buffed, and re-spawned every time its pawn dies while encounter HP remains.
///
/// Why a service of its own: BossManager owns encounter STATE (hp/phases/threat),
/// this owns encounter PRESENCE (pawn lifecycle). Separating them means a future
/// entity-based or multi-avatar implementation swaps in without touching combat logic.
/// </summary>
public sealed class BossAvatarService
{
    public const string BossClanTag = "WC2_BOSS";

    private readonly ILogger _logger;
    private BossesFileConfig _config;
    /// <summary>Set by Plugin.Load; used to schedule delayed model application.</summary>
    public CounterStrikeSharp.API.Core.BasePlugin? Plugin { get; set; }
    private bool _modelAppliedThisLife;
    private int _modelMismatchBackoff;
    private ActiveBoss? _boss;            // encounter we're embodying
    private bool _waitingForBot;          // bot_add issued, waiting for it to join
    private int _pendingRespawns;

    public BossAvatarService(BossesFileConfig config, ILogger logger)
    { _config = config; _logger = logger; }

    public void ApplyConfig(BossesFileConfig config) => _config = config;

    /// <summary>Called every boss tick (0.5s). The Warcraft plugin gives bots races whose
    /// abilities can reset the pawn's model (SetDefaultAppearance); we re-assert the boss
    /// look every ~2s so the encounter always renders as the boss, not the race default.</summary>
    private int _reassertCounter;
    public void ReassertAppearance()
    {
        if (_boss?.Avatar is not { IsValid: true } avatar) return;
        if (++_reassertCounter % 4 != 0) return; // every 4th tick = ~2s
        ApplyBuffs(avatar);
    }
    public bool IsAvatar(CCSPlayerController? p) => p is { IsValid: true, IsBot: true } && p.Clan == BossClanTag;

    // ── Lifecycle (driven by WC2BossesPlugin) ──────────────────

    public void Attach(ActiveBoss boss)
    {
        _boss = boss;
        _pendingRespawns = 0;
        // Prefer an existing idle bot; otherwise ask the engine for one and
        // finish possession when it spawns (OnBotSpawned).
        var bot = FindFreeBot();
        if (bot is not null) { Possess(bot); }
        else
        {
            _waitingForBot = true;
            Server.ExecuteCommand("bot_quota_mode normal");
            Server.ExecuteCommand($"bot_add_{(Random.Shared.Next(2) == 0 ? "t" : "ct")}");
            _logger.LogInformation("[WC2] No free bot; requested one for {Boss}", boss.Definition.Id);
        }

        // Verification: 8s later the boss must have a live, valid body. A ghost HP bar
        // with no boss (possession silently failing) is worse than no boss — re-attach
        // once, and if that also fails, give up loudly so the encounter doesn't haunt
        // the HUD for rounds on end.
        Plugin?.AddTimer(8f, () =>
        {
            if (_boss != boss) return; // resolved/replaced meanwhile
            var av = boss.Avatar;
            if (av is { IsValid: true } && av.PawnIsAlive) return; // all good

            _logger.LogWarning("[WC2] Avatar verification failed for {Boss} (avatar={State}); re-attaching once",
                boss.Definition.Id, av is null ? "null" : av.IsValid ? "dead" : "invalid");
            var retryBot = FindFreeBot();
            if (retryBot is not null) { Possess(retryBot); return; }

            _logger.LogError("[WC2] Re-attach failed for {Boss} — no claimable bot; despawning ghost encounter", boss.Definition.Id);
            OnAttachFailed?.Invoke(boss.Definition.Id);
        });
    }

    /// <summary>Raised when an avatar could not be attached at all — the owner (BossManager)
    /// should despawn the encounter rather than leave a ghost HP bar.</summary>
    public event Action<string>? OnAttachFailed;

    public void Detach(string reason)
    {
        var avatar = _boss?.Avatar;
        _boss = null;
        _waitingForBot = false;
        if (avatar is { IsValid: true })
        {
            avatar.Clan = "";
            // Kick by USERID: bot_kick by name silently fails on the boss's unicode rename
            // ("Hachirō..."), which left the bot alive post-"death", picking up a gun and
            // fighting on until killed manually. kickid is name-agnostic and reliable.
            if (avatar.UserId is { } uid) Server.ExecuteCommand($"kickid {uid}");
            else if (avatar.PlayerPawn.Value is { } pw) pw.CommitSuicide(false, true);
        }
        _logger.LogInformation("[WC2] Boss avatar released ({Reason})", reason);
    }

    /// <summary>Hook: a bot finished spawning. Claims it if we're waiting, or
    /// re-buffs it if it IS our avatar respawning mid-encounter.</summary>
    public void OnBotSpawned(CCSPlayerController bot)
    {
        if (_boss is null) return;
        if (_waitingForBot && !IsAvatar(bot)) { _waitingForBot = false; Possess(bot); return; }
        if (IsAvatar(bot)) { _modelAppliedThisLife = false; ApplyBuffs(bot); }  // respawned → re-dress
    }

    /// <summary>Hook: our avatar's pawn died but encounter HP remains → theatrical
    /// "phase blink": announce and force a respawn shortly after.</summary>
    public void OnAvatarPawnDeath(CCSPlayerController avatar, BasePlugin plugin)
    {
        if (_boss is null || !IsAvatar(avatar)) return;
        _pendingRespawns++;
        if (_pendingRespawns > 20) // safety valve against respawn loops on broken maps
        {
            _logger.LogWarning("[WC2] Avatar respawn limit hit; releasing avatar.");
            Detach("respawn_limit");
            return;
        }
        Server.PrintToChatAll($" \x02[BOSS]\x01 {_boss.Definition.DisplayName} vanishes in smoke... it is NOT over!");
        plugin.AddTimer(2.5f, () =>
        {
            if (_boss is null || avatar is not { IsValid: true }) return;
            avatar.Respawn();
            // Buffs re-applied by OnBotSpawned when the pawn is live again.
        });
    }

    // ── Internals ──────────────────────────────────────────────

    private CCSPlayerController? FindFreeBot()
    {
        // Prefer a T-side bot: with mp_humanteam CT keeping humans on CT, bosses then
        // consistently rise on the enemy side and no human can be near the claim.
        CCSPlayerController? fallback = null;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true, IsBot: true, IsHLTV: false } || p.Clan == BossClanTag) continue;
            if (p.TeamNum == (byte)CsTeam.Terrorist) return p;
            fallback ??= p;
        }
        return fallback;
    }

    private void Possess(CCSPlayerController bot)
    {
        if (_boss is null) return;
        _boss.Avatar = bot;
        _modelAppliedThisLife = false;

        // Kill an already-alive claimed bot BEFORE tagging it as the avatar — the pawn-death
        // hook checks IsAvatar (clan tag), and killing it after tagging would fire the
        // "vanishes in smoke" phase-blink handler + a racing second respawn.
        var wasAlive = bot.PawnIsAlive;
        if (wasAlive && bot.PlayerPawn.Value is { } livePawn)
            livePawn.CommitSuicide(false, true);

        bot.Clan = BossClanTag;
        // Capture the name as a LOCAL now, not read from the mutable _boss field inside the
        // deferred callback below: if the encounter changes (despawn/re-spawn) before next
        // frame runs — entirely possible with region retries, the zombie-watchdog, and finale
        // spawns all able to fire close together — _boss could be null or already a DIFFERENT
        // boss by then, throwing before the rename line ever executes. That silently left the
        // bot wearing its normal name with no error visible in normal play.
        var expectedName = _boss.Definition.DisplayName;
        Server.NextFrame(() =>
        {
            try
            {
                if (bot is not { IsValid: true }) return;
                // Rename so killfeed/scoreboard read like an encounter, not "Bot Chet".
                bot.PlayerName = expectedName;
                Utilities.SetStateChanged(bot, "CBasePlayerController", "m_iszPlayerName");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[WC2] Boss avatar rename (deferred) failed"); }
        });

        // Respawn on a DELAY, never in the same frame as the suicide: killing a pawn and
        // respawning it while the engine is still processing the death is native-crash
        // (segfault) territory. 0.5s lets the death settle; OnBotSpawned then re-dresses
        // the fresh pawn (model, scale, buffs) via the IsAvatar path.
        void DoRespawn()
        {
            if (_boss is null || bot is not { IsValid: true }) return;
            if (!bot.PawnIsAlive) bot.Respawn();
        }
        if (Plugin is { } plug) plug.AddTimer(wasAlive ? 0.6f : 0.1f, DoRespawn);
        else Server.NextFrame(DoRespawn); // fallback: at least not the same frame as the suicide

        Server.PrintToChatAll($" \x02[BOSS]\x01 {_boss.Definition.DisplayName} takes physical form!");
        _logger.LogInformation("[WC2] Possessed bot as {Boss}", _boss.Definition.Id);
    }

    private void ApplyBuffs(CCSPlayerController bot)
    {
        if (_boss is null) return;
        var pawn = bot.PlayerPawn.Value;
        if (pawn is null) return;

        // Pawn HP is a large buffer; real fight length lives in encounter HP.
        // Pawn HP is a large buffer, decoupled from encounter HP. Raised well above what a
        // simultaneous multi-player knife-only burst could deal in one instant — a merged
        // Final Stand raid (10+ players all hitting the same target at once) could otherwise
        // zero a small buffer before our reactive top-up ever runs. In bomb-defusal mode,
        // the boss's PAWN actually dying (its team's last member) ends the round instantly
        // via elimination — regardless of the real encounter HP pool — so this must never
        // happen except when we intend the encounter to end.
        pawn.Health = 50000;
        pawn.MaxHealth = 50000;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");

        pawn.VelocityModifier = _boss.Definition.MoveSpeed + _boss.SpeedBonus;

        // Finale: the boss stands ALONE on T while everyone else is swept to CT.
        if (_boss.ForceEnemyTeam && bot.TeamNum != (byte)CsTeam.Terrorist)
        {
            bot.SwitchTeam(CsTeam.Terrorist);
            if (!bot.PawnIsAlive) bot.Respawn();
        }

        ApplyModelOncePerLife(bot);

        // Self-healing name reassert: if the one-shot rename in Possess() ever missed (the
        // race described there), this catches it within one ReassertAppearance cycle (~2s)
        // instead of leaving the boss silently wearing its normal bot name for the whole fight.
        var expected = _boss.Definition.DisplayName;
        if (bot.PlayerName != expected)
        {
            bot.PlayerName = expected;
            Utilities.SetStateChanged(bot, "CBasePlayerController", "m_iszPlayerName");
        }

        // Make the engine's bot brain aggressive for this one.
        Server.ExecuteCommand("bot_chatter off");

        EnsureKnifeOnly(bot);
    }

    /// <summary>Model handling: SetModel reinitializes the skeleton, and doing that
    /// repeatedly (or too early in the pawn's life) freezes the animgraph in a T-pose.
    /// So: apply exactly once per pawn-life, 0.3s after spawn (the pattern skin plugins
    /// use), and afterwards only re-apply if the engine reports a DIFFERENT model
    /// (e.g. a Warcraft race ability overrode it), throttled to every ~20s.</summary>
    private void ApplyModelOncePerLife(CCSPlayerController bot)
    {
        var target = _boss?.Definition.Model;
        if (string.IsNullOrEmpty(target)) return;

        if (!_modelAppliedThisLife)
        {
            _modelAppliedThisLife = true;
            void Apply()
            {
                var pawn = _boss?.Avatar?.PlayerPawn.Value;
                if (pawn is null || _boss is null) return;
                var current = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
                _logger.LogInformation("[WC2] Boss model apply: engine='{Cur}' target='{Target}'", current, target);
                pawn.SetModel(target);
                // Scale once, right after the model — repeated skeleton writes are what freeze animgraphs.
                if (_config.ModelScale > 0)
                    pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().Scale = _config.ModelScale;
            }
            // 0.85s: deliberately AFTER WarcraftPlugin's +0.35s class-model re-apply (our own
            // bot-redress patch) — at 0.3s ours fired first and the class model stomped it,
            // leaving bosses wearing Paladin/Mage/etc. skins. Last writer wins; be last.
            if (Plugin is not null) Plugin.AddTimer(0.85f, Apply); else Apply();
            return;
        }

        // Watch for external overrides, gently.
        if (++_modelMismatchBackoff % 3 != 0) return; // every ~6s at the 2s reassert rate
        var p2 = bot.PlayerPawn.Value;
        var cur = p2?.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
        if (p2 is not null && cur is not null &&
            !cur.Contains(GetModelFileStem(target), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[WC2] Boss model overridden (engine='{Cur}'), re-applying.", cur);
            p2.SetModel(target);
        }
    }

    private static string GetModelFileStem(string path)
    {
        var file = path;
        var slash = file.LastIndexOf('/');
        if (slash >= 0) file = file[(slash + 1)..];
        var dot = file.IndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    /// <summary>Bosses fight with claws, not rifles: damage comes from abilities and melee.
    /// Runs on every buff pass, so anything the bot buys or picks up is stripped within ~2s.</summary>
    private void EnsureKnifeOnly(CCSPlayerController bot)
    {
        if (!_config.KnifeOnly) return;
        var weapons = bot.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons is null) return;

        bool hasKnife = false, hasGun = false;
        foreach (var handle in weapons)
        {
            var w = handle.Value;
            if (w is null || !w.IsValid) continue;
            var name = w.DesignerName ?? "";
            if (name.Contains("knife") || name.Contains("bayonet")) hasKnife = true;
            else if (name != "weapon_c4") hasGun = true;
        }

        if (hasGun) { bot.RemoveWeapons(); hasKnife = false; }
        if (!hasKnife) bot.GiveNamedItem("weapon_knife");
    }
}
