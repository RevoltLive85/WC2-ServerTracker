using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Bosses.Models;
using WC2.Shared.Extensions;

namespace WC2.Bosses.Services;

/// <summary>
/// Owns the full encounter lifecycle: spawn rules → health scaling → damage &
/// threat tracking → phase transitions → death → loot distribution announcements.
/// It knows nothing about UI, economy or quests — it only publishes events.
/// </summary>
public sealed class BossManager : IBossService
{
    private readonly IWc2EventBus _bus;
    private readonly BossAbilityRegistry _abilities;
    private readonly BossAvatarService _avatars;
    private readonly ILogger _logger;
    private BossesFileConfig _config;
    private ActiveBoss? _active;
    private DateTime _lastAvatarAliveUtc = DateTime.UtcNow;
    private DateTime _lastDeathUtc = DateTime.MinValue;

    public BossManager(IWc2EventBus bus, BossAbilityRegistry abilities, BossAvatarService avatars,
        BossesFileConfig config, ILogger logger)
    {
        _bus = bus; _abilities = abilities; _avatars = avatars; _config = config; _logger = logger;
    }

    public void ApplyConfig(BossesFileConfig config) => _config = config;

    // ── IBossService ───────────────────────────────────────────

    public IReadOnlyList<BossDefinition> GetDefinitions() => _config.Bosses;
    public ActiveBossSnapshot? GetActiveBoss() => _active?.Snapshot();
    public void ReloadDefinitions() { /* Plugin.cs re-reads file then calls ApplyConfig */ }

    public bool SpawnBoss(string bossId, string? reason = null, float healthMultiplier = 1f)
    {
        if (_active is not null)
        {
            _logger.LogWarning("[WC2] SpawnBoss({Id}) blocked: '{Active}' is already active ({Age:F0}s old)",
                bossId, _active.Definition.Id, (DateTime.UtcNow - _active.SpawnedUtc).TotalSeconds);
            return false;
        }

        BossDefinition? def = null;
        foreach (var b in _config.Bosses)
            if (string.Equals(b.Id, bossId, StringComparison.OrdinalIgnoreCase)) { def = b; break; }
        if (def is null) { _logger.LogWarning("[WC2] Unknown boss id {Id}", bossId); return false; }

        var players = PlayerExtensions.RealPlayerCount();
        if (reason != "admin" && reason != "finale" && players < def.MinPlayers)
        {
            _logger.LogInformation("[WC2] Boss {Id} needs {Min} players, have {Now}", def.Id, def.MinPlayers, players);
            return false;
        }
        if ((DateTime.UtcNow - _lastDeathUtc).TotalMinutes < _config.RespawnCooldownMinutes
            && reason != "admin" && reason != "finale")
            return false;

        var maxHp = (long)((def.BaseHealth + def.HealthPerPlayer * players) * Math.Max(0.1f, healthMultiplier));
        _lastAvatarAliveUtc = DateTime.UtcNow;
        _active = new ActiveBoss
        {
            Definition = def, MaxHealth = maxHp, CurrentHealth = maxHp,
            ForceEnemyTeam = string.Equals(reason, "finale", StringComparison.OrdinalIgnoreCase)
        };

        foreach (var line in def.SpawnLines)
            Server.PrintToChatAll($" \x10[WORLD]\x01 {line}");

        _avatars.Attach(_active);
        _bus.Publish(new BossSpawnedEvent(_active.Snapshot()));
        _logger.LogInformation("[WC2] Boss {Id} spawned with {Hp} HP for {Players} players ({Reason})",
            def.Id, maxHp, players, reason ?? "auto");
        return true;
    }

    public bool DespawnActiveBoss(string reason)
    {
        if (_active is null) return false;
        var snap = _active.Snapshot();
        _active = null;
        _avatars.Detach(reason);
        _bus.Publish(new WorldEventEndedEvent($"boss:{snap.BossId}", reason));
        return true;
    }

    // ── Combat plumbing (called from Plugin.cs game hooks) ─────

    /// <summary>Routes damage dealt to the boss avatar into encounter health,
    /// keeping the real pawn alive so the fight length is data-driven, not HP-100 driven.</summary>
    public void OnAvatarDamaged(CCSPlayerController? attacker, int damage)
    {
        if (_active is null || damage <= 0) return;
        var boss = _active;

        // Keep the avatar pawn topped up; encounter HP is our source of truth.
        // Threshold raised to match the higher HP ceiling (see BossAvatarService.ApplyBuffs) —
        // this is the reactive safety net, but the real defense against a burst zeroing the
        // pawn between hits is simply giving it far more buffer than any single-tick burst
        // of knife damage could plausibly deal.
        var pawn = boss.Avatar?.PlayerPawn.Value;
        if (pawn is not null && pawn.Health < 25000)
        {
            pawn.Health = 50000;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        boss.CurrentHealth = Math.Max(0, boss.CurrentHealth - damage);

        if (attacker.IsRealPlayer())
        {
            var id = attacker!.SteamID;
            boss.DamageBySteamId.TryGetValue(id, out var total);
            boss.DamageBySteamId[id] = total + damage;
            boss.Threat.TryGetValue(id, out var threat);
            boss.Threat[id] = threat + damage;                    // threat = damage (v1)
            _bus.Publish(new BossDamagedEvent(boss.Snapshot(), id, damage));
        }

        CheckPhaseTransition(boss);
        if (boss.CurrentHealth <= 0) Kill(boss);
    }

    private void CheckPhaseTransition(ActiveBoss boss)
    {
        var frac = (float)boss.CurrentHealth / boss.MaxHealth;
        var next = boss.PhaseIndex;
        for (var i = boss.PhaseIndex + 1; i < boss.Definition.Phases.Count; i++)
            if (frac <= boss.Definition.Phases[i].HealthThreshold) next = i;

        if (next == boss.PhaseIndex) return;
        boss.PhaseIndex = next;
        _bus.Publish(new BossPhaseChangedEvent(boss.Snapshot(), boss.Phase.Name));
    }

    /// <summary>Ticked ~every 0.5s by Plugin.cs — cheap: one time compare, no allocations.</summary>
    public void Tick(float now)
    {
        _abilities.TickEffects(now);   // slows expire/re-assert even between casts
        if (_active is null) return;
        var boss = _active;

        // Zombie-encounter guard: if the avatar (the boss's body) is gone or has been dead
        // for a few seconds while encounter HP remains, the fight is unwinnable — the body
        // can't be attacked. Clear it so it stops blocking the next region boss and stops
        // showing a phantom HP bar. Grace period covers the legit phase-blink respawn window.
        var avatar = boss.Avatar;
        var avatarOk = avatar is { IsValid: true } && avatar.PawnIsAlive;
        if (avatarOk) _lastAvatarAliveUtc = DateTime.UtcNow;
        else if ((DateTime.UtcNow - _lastAvatarAliveUtc).TotalSeconds > 8)
        {
            _logger.LogWarning("[WC2] Zombie encounter '{Boss}' (avatar dead/missing 8s+) — clearing.", boss.Definition.Id);
            DespawnActiveBoss("zombie_avatar_lost");
            return;
        }

        _avatars.ReassertAppearance();
        if (now < boss.NextAbilityTime) return;

        boss.NextAbilityTime = now + boss.Phase.AbilityIntervalSeconds;
        var list = boss.Phase.Abilities;
        if (list.Count == 0) return;
        var ability = list[Random.Shared.Next(list.Count)];
        if (!_abilities.TryCast(ability, boss))
            _logger.LogWarning("[WC2] Unknown boss ability id '{Ability}'", ability);
    }

    private void Kill(ActiveBoss boss)
    {
        _active = null;
        _lastDeathUtc = DateTime.UtcNow;
        _avatars.Detach("killed");

        foreach (var line in boss.Definition.DeathLines)
            Server.PrintToChatAll($" \x06[WORLD]\x01 {line}");

        _bus.Publish(new BossKilledEvent(boss.Snapshot(), boss.DamageBySteamId));
    }
}
