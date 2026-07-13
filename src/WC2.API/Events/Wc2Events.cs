using WC2.API.Models;

namespace WC2.API.Events;

// ── Boss lifecycle ─────────────────────────────────────────────
public sealed record BossSpawnedEvent(ActiveBossSnapshot Boss);
public sealed record BossPhaseChangedEvent(ActiveBossSnapshot Boss, string PhaseName);
public sealed record BossDamagedEvent(ActiveBossSnapshot Boss, ulong AttackerSteamId, long Damage);
public sealed record BossKilledEvent(ActiveBossSnapshot Boss, IReadOnlyDictionary<ulong, long> DamageBySteamId);

// ── World ──────────────────────────────────────────────────────
public sealed record RegionEnteredEvent(RegionDefinition Region, string MapName);

// ── Economy ────────────────────────────────────────────────────
public sealed record CurrencyChangedEvent(ulong SteamId, CurrencyType Currency, long Delta, long NewBalance, string Source);
public sealed record LootAwardedEvent(ulong SteamId, LootDrop Drop, string Source);

// ── Quests ─────────────────────────────────────────────────────
public sealed record QuestCompletedEvent(ulong SteamId, QuestDefinition Quest);

// ── World events ───────────────────────────────────────────────
public sealed record WorldEventStartedEvent(string EventId, string DisplayName);
public sealed record WorldEventEndedEvent(string EventId, string Reason);

// ── Player flavor ──────────────────────────────────────────────
public sealed record KillStreakEvent(ulong SteamId, int Streak);
