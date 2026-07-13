# WC2 Framework — MMORPG layer for CS2 Warcraft servers

A modular, update-safe RPG framework built **on top of** (never inside)
[NightFuryPrime/CS2-Warcraft-Plugin](https://github.com/NightFuryPrime/CS2-Warcraft-Plugin).

**The pitch:** players join and immediately see a region discovery banner
("— The Volcanic Realm — *Even the shadows burn here*"), a WoW-style HUD with
race/level/gold, ambient world chatter, daily quests, and a server-wide boss
with phases, threat, a segmented health bar and rarity-colored loot toasts.

## Author
Built and maintained by **ServerTracker.live** (aka RevoltLive) for the
[wc2.servertracker.live](https://wc2.servertracker.live) CS2 Warcraft server.

## Modules
| Module | What it gives you |
|---|---|
| **WC2.API** | Contracts: interfaces, event records, models. The only shared dependency. |
| **WC2.Shared** | Event bus, JSON config store, object pool, reflection bridge to the Warcraft plugin. |
| **WC2.Bosses** | Data-driven encounters: HP scales with players, phases, threat, ability registry, spawn/death flavor. |
| **WC2.Economy** | Gold / Boss Tokens / **Worldstone Shards**, vendor (`css_shop`), weighted loot tables, contribution-scaled boss loot. |
| **WC2.UI** | HUD compositor (single CenterHtml writer), boss frame with ▰▰▱ bar, toasts, cinematic banners, kill streaks. |
| **WC2.World** | Maps → regions with difficulty, XP/gold bonuses, ambient lines, auto region-boss spawns. |
| **WC2.Quests** | Deterministic daily rotation + weeklies, JSON-persisted progress, rewards through economy + Warcraft XP. |
| **WC2.Events** | Double XP, Gold Rush, Treasure Goblin, Invasions; random per-round rotation; global XP/gold multipliers. |
| **WC2.Admin** | In-game WASD admin menu: bosses, events, maps, player grants (gold/tokens/shards/XP/slay/kick), reload-all. |

## Install
```bash
./WC2-Framework.sln.build.sh
# copy each module's bin output to:
# csgo/addons/counterstrikesharp/plugins/<ModuleName>/
```
Load order does not matter — modules discover each other lazily.
First run writes default configs to `addons/counterstrikesharp/wc2-configs/`.

## Commands
Player: `!quests` `!gold` `!vendor`/`!market` `!buy <id>` `!skins` `!thirdperson`/`!tp`
Admin (`@css/root`): `!admin` (WASD menu) · `!wc_boss <id>` `!wc_boss_kill` `!wc_event <id>`
`!wc_event_stop` `!wc_nextmap [map]` `!wc_testmodel <path>` `!wc_reload_bosses|economy|regions|quests|events`

## Recommended repo layout (multi-repo, as designed)
```
WC2-Framework/
├── src/WC2.API … WC2.Events
└── vendor/NightFuryPrime.Warcraft   (git submodule — never modified)
```
When upstream updates: `git submodule update --remote`, rebuild, done.
If a bridge probe misses a renamed member, the framework logs a warning and
degrades gracefully instead of crashing — then update one probe list in
`WC2.Shared/WarcraftReflectionBridge.cs`.

**Note:** in this deployment WarcraftPlugin is a direct fork (not a submodule),
with a small, documented set of upstream patches — see `docs/WARCRAFTPLUGIN-PATCHES.md`.
Those changes are intentionally minimal and easy to re-apply after pulling upstream updates.

## Docs
See `docs/ARCHITECTURE.md` for the module contract, event flow, and performance notes.
