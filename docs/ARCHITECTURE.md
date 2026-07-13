# WC2 Framework — Architecture

## The one rule
**No module references another module.** Every module references only `WC2.API`
(contracts) and `WC2.Shared` (plumbing). Modules find each other at runtime via
CounterStrikeSharp **PluginCapabilities** and talk via the **Wc2EventBus**.

```
              ┌────────────────────────────────────────────┐
              │                WC2.API                     │
              │  interfaces · event records · models       │
              └───────▲───────▲───────▲───────▲───────▲────┘
                      │       │       │       │       │
   WC2.Bosses   WC2.Economy  WC2.UI  WC2.World  WC2.Quests   WC2.Events
                      │
              ┌───────▼────────────────────────────────────┐
              │              WC2.Shared                    │
              │  EventBus · JsonConfigStore · ObjectPool   │
              │  WarcraftReflectionBridge                  │
              └───────┬────────────────────────────────────┘
                      │  reflection only, no compile-time ref
              NightFuryPrime/CS2-Warcraft-Plugin (untouched)
```

## Why each piece exists
| Piece | Reason |
|---|---|
| `Wc2Capabilities` | Load-order-independent service discovery. Any module can be missing; consumers get `null` and degrade. |
| `Wc2EventBus` | Fire-and-forget domain events (`BossKilledEvent`…). Publisher never knows who listens → modules stay independent. |
| `WarcraftReflectionBridge` | Anti-corruption layer. The upstream plugin stays a git submodule that can update freely; only the bridge's member-probe lists ever need touching. |
| `JsonConfigStore` | One shared `wc2-configs/` directory, self-writing defaults, hot reload via `css_wc_reload_*`. Zero gameplay values in code. |
| `HudService` (single compositor) | Only one thing may write CenterHtml, or modules overwrite each other. Widgets are polled providers sorted by slot+priority. |
| `BossManager` encounter HP | Boss health is virtual (data-driven longs), decoupled from the 100-HP pawn — enabling 20k HP fights, phases and threat. |
| Deterministic daily quests | Seeded by `(steamId, date)` — no scheduler, no sync, reconnect-safe. |
| Write-behind persistence | Wallet/quest saves are dirty-flagged and flushed on timers/round end; the kill hot path never touches disk. |

## Performance posture (64p)
- No per-frame work at all: HUD 0.5s, boss tick 0.5s, events 1s.
- No LINQ in hot paths; pooled StringBuilders; preallocated dictionaries.
- Event bus publishes over a snapshot array — zero allocation per publish.

## Extending
1. New boss/region/quest/event **content** → edit JSON, `css_wc_reload_*`. No code.
2. New boss **ability** → one method in `BossAbilityRegistry`.
3. New world **event type** → one `IWorldEventHandler` class + one registry line.
4. New **module** (e.g. WC2.Crafting) → reference API+Shared, register a capability, subscribe to events. Nothing else changes.
