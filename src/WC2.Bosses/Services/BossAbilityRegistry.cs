using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WC2.Bosses.Models;
using WC2.Shared.Extensions;
using System.Drawing;

namespace WC2.Bosses.Services;

/// <summary>
/// Boss abilities v2 — modeled on how WarcraftPlugin classes actually do it:
/// every ability has VISIBLE feedback (particles + sounds + screen shake),
/// slows are EFFECTS re-applied every tick (the engine resets VelocityModifier
/// constantly, so one-shot writes do nothing), and impacts move players.
/// Particle/sound names are taken verbatim from WarcraftPlugin's own classes,
/// so they are proven to exist on this server.
/// </summary>
public sealed class BossAbilityRegistry
{
    public delegate void BossAbility(ActiveBoss boss);
    private readonly Dictionary<string, BossAbility> _abilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly BasePlugin _plugin;

    // ── proven asset names (verbatim from WarcraftPlugin sources) ──
    private const string FxExplosion = "particles/explosions_fx/explosion_hegrenade_dirt_ground.vpcf";
    private const string FxDarkSmoke = "particles/survival_fx/danger_zone_loop_black.vpcf";
    private const string FxGreenBurst = "particles/critters/chicken/chicken_impact_burst_zombie.vpcf";
    private const string SndWhoosh = "BulletBy.Subsonic";
    private const string SndHurt = "Player.DamageFall.Fem";
    private const string SndAlert = "UI.PlayerPingUrgent";

    // ── timed slow effects, re-applied every boss tick ──
    private readonly Dictionary<ulong, (float Until, float Multiplier)> _slows = new(32);

    public BossAbilityRegistry(BasePlugin plugin)
    {
        _plugin = plugin;
        _abilities["flame_nova"]  = FlameNova;
        _abilities["magma_leap"]  = MagmaLeap;
        _abilities["frost_slow"]  = FrostSlow;
        _abilities["blizzard"]    = Blizzard;
        _abilities["enrage"]      = Enrage;
        _abilities["ground_slam"] = GroundSlam;
        _abilities["life_drain"]  = LifeDrain;
    }

    public bool TryCast(string id, ActiveBoss boss)
    {
        if (!_abilities.TryGetValue(id, out var ability)) return false;
        ability(boss);
        return true;
    }

    /// <summary>Called every boss tick (0.5s): keeps slows alive against engine resets.</summary>
    public void TickEffects(float now)
    {
        if (_slows.Count == 0) return;
        List<ulong>? expired = null;
        foreach (var p in PlayerExtensions.RealPlayers())
        {
            if (!_slows.TryGetValue(p.SteamID, out var slow)) continue;
            var pawn = p.PlayerPawn.Value;
            if (now >= slow.Until || pawn is null || pawn.Health <= 0)
            {
                (expired ??= new()).Add(p.SteamID);
                if (pawn is not null) pawn.VelocityModifier = 1f;
                continue;
            }
            pawn.VelocityModifier = slow.Multiplier; // re-assert against engine reset
        }
        if (expired is not null) foreach (var id in expired) _slows.Remove(id);
    }

    private void ApplySlow(CCSPlayerController player, float seconds, float multiplier) =>
        _slows[player.SteamID] = (Server.CurrentTime + seconds, multiplier);

    // ── shared helpers (native equivalents of WarcraftPlugin's Warcraft.* helpers) ──

    private void SpawnParticle(Vector pos, string effect, float lifetime = 2f)
    {
        var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle is null) return;
        particle.EffectName = effect;
        particle.StartActive = true;
        particle.Teleport(pos, new QAngle(), null);
        particle.DispatchSpawn();
        particle.AcceptInput("Start");
        _plugin.AddTimer(lifetime, () => { if (particle.IsValid) particle.Remove(); });
    }

    private void Shake(Vector pos, float radius, float amplitude = 12f, float duration = 1f)
    {
        var shake = Utilities.CreateEntityByName<CEnvShake>("env_shake");
        if (shake is null) return;
        shake.Amplitude = amplitude;
        shake.Frequency = 200f;
        shake.Duration = duration;
        shake.Radius = radius;
        shake.Teleport(pos, new QAngle(), null);
        shake.DispatchSpawn();
        shake.AcceptInput("StartShake");
        _plugin.AddTimer(duration + 0.5f, () => { if (shake.IsValid) shake.Remove(); });
    }

    private static void Hurt(CCSPlayerController victim, int damage, string centerText)
    {
        var pawn = victim.PlayerPawn.Value;
        if (pawn is null || pawn.Health <= 0) return;
        pawn.Health = Math.Max(1, pawn.Health - damage); // boss auras never one-shot
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        pawn.EmitSound(SndHurt, volume: 0.4f);
        victim.PrintToCenter(centerText);
    }

    private static float Distance(Vector a, Vector b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static Vector? BossOrigin(ActiveBoss boss) => boss.Avatar?.PlayerPawn.Value?.AbsOrigin;

    // ══ ABILITIES ══════════════════════════════════════════════

    private void FlameNova(ActiveBoss boss)
    {
        if (BossOrigin(boss) is not { } origin) return;
        SpawnParticle(origin, FxExplosion, 2f);
        boss.Avatar?.PlayerPawn.Value?.EmitSound(SndWhoosh, volume: 0.8f);
        Shake(origin, 500f, amplitude: 10f, duration: 0.8f);

        foreach (var p in PlayerExtensions.RealPlayers())
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn?.AbsOrigin is null || pawn.Health <= 0) continue;
            var dist = Distance(origin, pawn.AbsOrigin);
            if (dist > 450f) continue;
            // damage falls off with distance: 24 at point blank → ~8 at edge
            var dmg = (int)((24f - 16f * (dist / 450f)) * boss.Definition.DamageMultiplier);
            SpawnParticle(pawn.AbsOrigin, FxExplosion, 1f);
            Hurt(p, dmg, "🔥 Flame Nova sears you!");
        }
    }

    private void MagmaLeap(ActiveBoss boss)
    {
        var snap = boss.Snapshot();
        if (snap.TopThreatSteamId is not { } steamId) return;
        var avatarPawn = boss.Avatar?.PlayerPawn.Value;
        if (avatarPawn?.AbsOrigin is null) return;

        foreach (var p in PlayerExtensions.RealPlayers())
        {
            if (p.SteamID != steamId) continue;
            var targetPawn = p.PlayerPawn.Value;
            if (targetPawn?.AbsOrigin is null || targetPawn.Health <= 0) break;

            SpawnParticle(avatarPawn.AbsOrigin, FxDarkSmoke, 1.5f);          // departure
            var dest = new Vector(targetPawn.AbsOrigin.X + 72f, targetPawn.AbsOrigin.Y, targetPawn.AbsOrigin.Z + 8f);
            avatarPawn.Teleport(dest, null, null);
            SpawnParticle(dest, FxExplosion, 2f);                             // impact
            avatarPawn.EmitSound(SndAlert, volume: 0.7f);
            Shake(dest, 400f, amplitude: 14f, duration: 1f);
            Hurt(p, (int)(15 * boss.Definition.DamageMultiplier), "💥 The boss crashes down on you!");
            Server.PrintToChatAll($" \x02[BOSS]\x01 {boss.Definition.DisplayName} leaps at {p.PlayerName}!");
            break;
        }
    }

    private void FrostSlow(ActiveBoss boss)
    {
        if (BossOrigin(boss) is not { } origin) return;
        SpawnParticle(origin, FxDarkSmoke, 2f);
        boss.Avatar?.PlayerPawn.Value?.EmitSound(SndWhoosh, volume: 0.6f);

        foreach (var p in PlayerExtensions.RealPlayers())
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn?.AbsOrigin is null || pawn.Health <= 0) continue;
            if (Distance(origin, pawn.AbsOrigin) > 550f) continue;
            ApplySlow(p, 3.5f, 0.55f);                     // ticker keeps it alive
            SpawnParticle(pawn.AbsOrigin, FxGreenBurst, 1f);
            Hurt(p, (int)(4 * boss.Definition.DamageMultiplier), "❄ Frost grips your legs...");
        }
    }

    private void Blizzard(ActiveBoss boss)
    {
        Server.PrintToChatAll(" \x0B[BOSS]\x01 A blizzard tears across the whole map!");
        // 3 pulses over 4 seconds — a channel, not a single silent hit
        for (var pulse = 0; pulse < 3; pulse++)
        {
            _plugin.AddTimer(pulse * 1.5f, () =>
            {
                foreach (var p in PlayerExtensions.RealPlayers())
                {
                    var pawn = p.PlayerPawn.Value;
                    if (pawn?.AbsOrigin is null || pawn.Health <= 0) continue;
                    SpawnParticle(pawn.AbsOrigin, FxGreenBurst, 1f);
                    Hurt(p, (int)(5 * boss.Definition.DamageMultiplier), "❄ The blizzard bites!");
                    ApplySlow(p, 1.2f, 0.8f);
                }
            });
        }
    }

    private void Enrage(ActiveBoss boss)
    {
        boss.SpeedBonus = 0.4f;                            // ApplyBuffs re-asserts every 2s
        var pawn = boss.Avatar?.PlayerPawn.Value;
        if (pawn is not null)
        {
            pawn.Render = Color.FromArgb(255, 255, 60, 60); // red tint
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
            pawn.EmitSound(SndAlert, volume: 1f);
            if (pawn.AbsOrigin is { } o) { SpawnParticle(o, FxExplosion, 1.5f); Shake(o, 600f, 16f, 1.2f); }
        }
        Server.PrintToChatAll($" \x02[BOSS]\x01 {boss.Definition.DisplayName} ENRAGES!");
    }

    private void GroundSlam(ActiveBoss boss)
    {
        if (BossOrigin(boss) is not { } origin) return;
        SpawnParticle(origin, FxExplosion, 2f);
        Shake(origin, 700f, amplitude: 20f, duration: 1.5f);
        boss.Avatar?.PlayerPawn.Value?.EmitSound(SndAlert, volume: 1f);

        foreach (var p in PlayerExtensions.RealPlayers())
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn?.AbsOrigin is null || pawn.Health <= 0) continue;
            var dist = Distance(origin, pawn.AbsOrigin);
            if (dist > 500f) continue;
            // knock players airborne, away from the boss
            var dx = pawn.AbsOrigin.X - origin.X; var dy = pawn.AbsOrigin.Y - origin.Y;
            var len = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            pawn.Teleport(null, null, new Vector(dx / len * 260f, dy / len * 260f, 320f));
            Hurt(p, (int)(12 * boss.Definition.DamageMultiplier), "⛰ GROUND SLAM sends you flying!");
        }
    }

    private void LifeDrain(ActiveBoss boss)
    {
        if (BossOrigin(boss) is not { } origin) return;

        // nearest living victim within range
        CCSPlayerController? victim = null; var best = 600f;
        foreach (var p in PlayerExtensions.RealPlayers())
        {
            var pawn = p.PlayerPawn.Value;
            if (pawn?.AbsOrigin is null || pawn.Health <= 0) continue;
            var d = Distance(origin, pawn.AbsOrigin);
            if (d < best) { best = d; victim = p; }
        }
        if (victim?.PlayerPawn.Value?.AbsOrigin is not { } victimPos) return;

        var drained = (int)(16 * boss.Definition.DamageMultiplier);
        SpawnParticle(victimPos, FxGreenBurst, 1.5f);
        SpawnParticle(origin, FxGreenBurst, 1.5f);
        boss.Avatar?.PlayerPawn.Value?.EmitSound(SndWhoosh, volume: 0.7f);
        Hurt(victim, drained, "🩸 Your life is being DRAINED!");

        boss.CurrentHealth = Math.Min(boss.MaxHealth, boss.CurrentHealth + drained * 15); // heals the boss bar
        Server.PrintToChatAll($" \x02[BOSS]\x01 {boss.Definition.DisplayName} drains {victim.PlayerName}'s life force!");
    }
}
