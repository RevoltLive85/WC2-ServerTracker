# Deploying the WC2 Framework to your CS2 server

## 0. Prerequisites on the server
1. **Metamod:Source 2.x** for CS2 → extract into `game/csgo/`, add
   `			Game	csgo/addons/metamod` to `game/csgo/gameinfo.gi` (SearchPaths).
2. **CounterStrikeSharp** (runtime + API) → extract into `game/csgo/addons/`.
3. **CS2-Warcraft-Plugin** (NightFuryPrime) installed and working first:
   `game/csgo/addons/counterstrikesharp/plugins/WarcraftPlugin/`.
   Verify with `css_plugins list` in server console before adding WC2.

## 1. Build (on your dev machine)
```bash
cd WC2-Framework
./WC2-Framework.sln.build.sh      # or: dotnet build -c Release
```
Requires .NET 8 SDK. The CounterStrikeSharp.API package restores from NuGet;
pin the version in the .csproj files to the CSSharp build running on your server.

## 2. Copy modules to the server
Each module = one plugin folder named after its DLL:
```
game/csgo/addons/counterstrikesharp/plugins/
├── WarcraftPlugin/          (existing, untouched)
├── WC2.Bosses/   ← src/WC2.Bosses/bin/Release/net8.0/*
├── WC2.Economy/
├── WC2.UI/
├── WC2.World/
├── WC2.Quests/
└── WC2.Events/
```
WC2.API.dll and WC2.Shared.dll are copied automatically into each module's
output — ship them alongside each plugin folder (CSSharp isolates plugin
load contexts, so duplicates are fine and intended).

Load order does not matter; modules find each other lazily via capabilities.

## 3. First boot
Start the server (or `css_plugins load` each module). On first load the
framework writes editable defaults to:
```
game/csgo/addons/counterstrikesharp/wc2-configs/
├── bosses.json  regions.json  quests.json
├── economy.json events.json   ui.json
```
Player data lands in `.../wc2-data/wallets/` and `.../wc2-data/quests/`.

## 4. Server cfg for boss avatars
Bosses possess bots, so bots must be allowed:
```
bot_quota 2
bot_quota_mode normal
bot_join_after_player 1
```
Maps need a nav mesh (all official maps have one; workshop maps may not).

## 5. Admin permissions
Add your SteamID64 to `addons/counterstrikesharp/configs/admins.json`:
```json
{ "yourname": { "identity": "7656119XXXXXXXXXX", "flags": ["@css/root"] } }
```

## 6. Smoke test (in order)
```
css_plugins list          → all 6 WC2 modules + WarcraftPlugin loaded
css_gold                  → balances print
css_quests                → daily quest log prints
css_wc_boss grommash_the_molten   (admin) → banner + bot renamed + boss bar
css_wc_event treasure_goblin      (admin) → world event banner
css_wc_reload_bosses      → hot reload works
```
Check console for `[WC2] Warcraft bridge bound: instance=True ...` — if it
logs degraded mode, update the probe names in WC2.Shared/WarcraftReflectionBridge.cs
to match your WarcraftPlugin version (one file, ~5 strings).

## 7. Updating the Warcraft plugin later
Replace the WarcraftPlugin folder / `git submodule update --remote`, restart.
WC2 modules keep working; at worst the bridge logs a warning until you refresh
its probe list. Never edit WarcraftPlugin sources.
