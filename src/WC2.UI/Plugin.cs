using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Shared;
using WC2.Shared.Configuration;
using WC2.Shared.Extensions;
using WC2.UI.Services;

namespace WC2.UI;

[MinimumApiVersion(200)]
public sealed class WC2UiPlugin : BasePlugin
{
    public override string ModuleName => "WC2.UI";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "HUD compositor, boss bars, toasts, cinematic banners.";

    private UiFileConfig _config = null!;
    private HudService _hud = null!;
    private IWc2EventBus _bus = null!;
    private readonly Dictionary<ulong, int> _killStreaks = new(64);
    private readonly HashSet<ulong> _welcomed = new(64);
    private readonly Dictionary<ulong, int> _lastKnownLevel = new(64);

    private static readonly string[] _flavorVerbs =
    {
        "cut down", "struck down", "vanquished", "sent to the shadowlands", "banished",
        "slew", "obliterated", "put to rest", "sundered"
    };
    private static readonly string[] _flavorHeadshotVerbs =
    {
        "delivered a killing blow to", "shattered the skull of", "executed",
        "landed a fatal strike on", "ended"
    };
    private readonly ThirdPersonService _thirdPerson = new();

    public override void Load(bool hotReload)
    {
        var store = new JsonConfigStore(ModuleDirectory, Logger);
        _config = store.LoadOrCreate("ui.json", UiFileConfig.Default);

        _bus = Wc2Bootstrap.EnsureCore(Logger);
        _hud = new HudService();
        Capabilities.RegisterPluginCapability(Wc2Capabilities.Hud, () => _hud);

        // ── Widgets (polled; lazily resolve sibling capabilities so load order never matters) ──
        _hud.SetWidget("boss_bar", HudSlot.Top, 100,
            p => new BossBarWidget(Wc2Capabilities.Bosses.GetOrNull(), _config.BossBarColor).Render(p));

        _hud.SetWidget("player_line", HudSlot.Bottom, 50, p =>
        {
            var wc = Wc2Capabilities.Warcraft.GetOrNull();
            var eco = Wc2Capabilities.Economy.GetOrNull();
            var world = Wc2Capabilities.World.GetOrNull();
            if (wc is null && eco is null) return null;

            var level = wc?.GetLevel(p.SteamID) ?? 0;
            var race = wc?.GetRaceName(p.SteamID) ?? "Adventurer";
            var gold = eco?.GetBalance(p.SteamID, CurrencyType.Gold) ?? 0;
            var region = world?.CurrentRegion?.DisplayName;

            return $"<font color='{_config.AccentColor}'>{race}</font> " +
                   $"<font color='#ffffff'>Lv {level}</font>  " +
                   $"<font color='#ffd35c'>⛃ {gold:N0}</font>" +
                   (region is null ? "" : $"  <font color='#9fdcff'>{region}</font>");
        });

        // ── Reactive flourishes (event-driven, WoW-flavored) ──
        _bus.Subscribe<BossSpawnedEvent>(e =>
        {
            _hud.Banner(e.Boss.DisplayName, e.Boss.Title + " has entered the realm", _config.BossBarColor, 6f);
            if (!string.IsNullOrWhiteSpace(_config.BossSpawnSound))
                foreach (var p in PlayerExtensions.RealPlayers())
                    try { p.ExecuteClientCommand($"play {_config.BossSpawnSound}"); } catch { /* cosmetic only */ }
        });
        _bus.Subscribe<BossPhaseChangedEvent>(e =>
        {
            if (Wc2Capabilities.Bosses.GetOrNull()?.GetDefinitions() is { } defs)
                foreach (var d in defs)
                    if (d.Id == e.Boss.BossId && d.Phases.Count > e.Boss.PhaseIndex &&
                        d.Phases[e.Boss.PhaseIndex].AnnounceHtml is { } html)
                        _hud.ToastAll(html, 4f);
        });
        _bus.Subscribe<BossKilledEvent>(e =>
            _hud.Banner("VICTORY", $"{e.Boss.DisplayName} has been slain!", "#7CFC91", 6f));
        _bus.Subscribe<RegionEnteredEvent>(e =>
            _hud.Banner($"— {e.Region.DisplayName} —", e.Region.Flavor, e.Region.ColorHex, 7f));
        _bus.Subscribe<QuestCompletedEvent>(e =>
            _hud.ToastAll($"<font color='#ffd35c'>Quest complete:</font> <font color='#fff'>{e.Quest.DisplayName}</font>", 4f));
        _bus.Subscribe<LootAwardedEvent>(e =>
        {
            foreach (var p in PlayerExtensions.RealPlayers())
                if (p.SteamID == e.SteamId)
                    _hud.Toast(p, $"<font color='{RarityColor(e.Drop.Rarity)}'>[{e.Drop.Rarity}]</font> " +
                                  $"<font color='#fff'>{e.Drop.DisplayName} ×{e.Drop.Amount}</font>", 3.5f);

            // Legendary finds are exciting enough to share with the whole server.
            if (_config.AnnounceLegendaryLoot && e.Drop.Rarity == LootRarity.Legendary)
            {
                var finder = PlayerExtensions.RealPlayers().FirstOrDefault(p => p.SteamID == e.SteamId);
                var name = finder?.PlayerName ?? "A hero";
                Server.PrintToChatAll($" \x02★ [WORLD]\x01 {name} has found a \x10{e.Drop.DisplayName}\x01! ★");
            }
        });
        _bus.Subscribe<WorldEventStartedEvent>(e =>
            _hud.Banner("WORLD EVENT", e.DisplayName, "#b58cff", 6f));

        // ── Welcome panel: styled text, once per connection on first spawn ──
        // Routed through the HUD compositor (single writer) so the per-tick repaint can't
        // stomp it — a separate plugin writing to center-html directly gets overwritten.
        if (_config.WelcomeEnabled)
        {
            var welcomeHtml = BuildWelcomeHtml();
            RegisterEventHandler<EventPlayerSpawn>((ev, _) =>
            {
                var p = ev.Userid;
                if (!p.IsRealPlayer()) return HookResult.Continue;
                if (!_welcomed.Add(p!.SteamID)) return HookResult.Continue; // once per connection
                AddTimer(1.0f, () =>
                {
                    if (p is { IsValid: true })
                        _hud.ShowWelcome(p, welcomeHtml, _config.WelcomeDurationSeconds);
                });
                return HookResult.Continue;
            });
            // Reconnecting player should be welcomed again.
            RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
            {
                if (ev.Userid is { IsValid: true } p) _welcomed.Remove(p.SteamID);
                return HookResult.Continue;
            });
        }

        // ── Kill streaks + flavored killfeed ──
        if (_config.ShowKillStreaks || _config.FlavoredKillfeed)
            RegisterEventHandler<EventPlayerDeath>((ev, _) =>
            {
                var a = ev.Attacker; var v = ev.Userid;
                if (v.IsRealPlayer()) _killStreaks[v!.SteamID] = 0;

                // Flavor fires whenever a HUMAN is involved on either side — bots have classes
                // too (GetRaceName works for them), but firing on every pure bot-vs-bot kill
                // with 14+ bots constantly fighting would flood chat non-stop. The previous
                // version required BOTH sides to be human, which meant it almost never fired
                // at all on a bot-heavy server (nearly every kill has a bot on one side).
                if (_config.FlavoredKillfeed && a is { IsValid: true } && v is { IsValid: true } && a != v
                    && (a.IsRealPlayer() || v.IsRealPlayer()))
                {
                    var wc = Wc2Capabilities.Warcraft.GetOrNull();
                    var race = wc?.GetRaceName(a.SteamID) ?? "Adventurer";
                    var verb = ev.Headshot
                        ? _flavorHeadshotVerbs[Random.Shared.Next(_flavorHeadshotVerbs.Length)]
                        : _flavorVerbs[Random.Shared.Next(_flavorVerbs.Length)];
                    Server.PrintToChatAll($" \x08{a.PlayerName}\x01 the \x0A{race}\x01 {verb} \x08{v.PlayerName}\x01");
                }

                if (_config.ShowKillStreaks && a.IsRealPlayer() && a != v)
                {
                    _killStreaks.TryGetValue(a!.SteamID, out var s);
                    _killStreaks[a.SteamID] = ++s;
                    if (s >= _config.KillStreakMinimum)
                    {
                        _bus.Publish(new KillStreakEvent(a.SteamID, s));
                        _hud.ToastAll($"<font color='#ff9d3c'><b>{a.PlayerName}</b> is on a {s}-kill rampage!</font>", 3f);
                    }
                }
                return HookResult.Continue;
            });

        AddTimer(_config.HudRefreshSeconds, _hud.Render,
            CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        // REPAINT EVERY TICK — proven requirement, do not "optimize" this again: CenterHtml
        // begins a fade-out almost immediately, and any repaint slower than per-tick shows a
        // blinking white background box between paints (tried 4Hz: text blinked, white box).
        // The per-tick cost is small (real players only); server-rate issues are bot/VPS load.
        RegisterListener<Listeners.OnTick>(_hud.Repaint);

        // ── Level-up detection: WarcraftPlugin grants XP internally (reflection bridge),
        // so we don't get a direct event — instead poll each player's live level on a light
        // 2s cadence and fire a fanfare the moment it increases. First-seen level for a
        // player is just recorded, never fires (avoids a false "level up" on connect).
        AddTimer(2f, () =>
        {
            var wc = Wc2Capabilities.Warcraft.GetOrNull();
            if (wc is null) return;
            foreach (var p in PlayerExtensions.RealPlayers())
            {
                var level = wc.GetLevel(p.SteamID);
                if (_lastKnownLevel.TryGetValue(p.SteamID, out var prev))
                {
                    if (level > prev)
                    {
                        _hud.Banner("LEVEL UP", $"{p.PlayerName} is now level {level}!", "#ffd35c", 5f);
                        if (!string.IsNullOrWhiteSpace(_config.LevelUpSound))
                            try { p.ExecuteClientCommand($"play {_config.LevelUpSound}"); } catch { }
                        Server.PrintToChatAll($" \x04[WORLD]\x01 {p.PlayerName} has reached level \x10{level}\x01!");
                    }
                    _lastKnownLevel[p.SteamID] = level;
                }
                else
                {
                    _lastKnownLevel[p.SteamID] = level; // first observation, just record it
                }
            }
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

        // ── !thirdperson — see your skin (no sv_cheats). Safe: clears on death/round/disconnect. ──
        AddCommand("css_thirdperson", "Toggle third-person view", (p, _) =>
        {
            if (p is not { IsValid: true }) return;
            if (!p.PawnIsAlive) { p.PrintToChat(" \x02[WC2]\x01 Third-person only works while alive."); return; }
            var on = _thirdPerson.Toggle(p);
            p.PrintToChat(on ? " \x04[WC2]\x01 Third-person ON — type !thirdperson again to turn off."
                             : " \x04[WC2]\x01 Third-person OFF.");
        });
        AddCommand("css_tp", "Toggle third-person view", (p, _) =>
        {
            if (p is not { IsValid: true }) return;
            if (!p.PawnIsAlive) { p.PrintToChat(" \x02[WC2]\x01 Third-person only works while alive."); return; }
            var on = _thirdPerson.Toggle(p);
            p.PrintToChat(on ? " \x04[WC2]\x01 Third-person ON — type !tp again to turn off."
                             : " \x04[WC2]\x01 Third-person OFF.");
        });

        // Safety: never leave a camera dangling. Clear on death, disconnect, round end, map change.
        RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } d) _thirdPerson.ForceClear(d.SteamID);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } d)
            {
                _thirdPerson.ForceClear(d.SteamID);
                _lastKnownLevel.Remove(d.SteamID);
            }
            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundEnd>((_, _) => { _thirdPerson.ClearAll(); return HookResult.Continue; });
        RegisterListener<Listeners.OnMapStart>(_ => _thirdPerson.ClearAll());

        Logger.LogInformation("[WC2] UI module loaded.");
    }

    private static string RarityColor(LootRarity r) => r switch
    {
        LootRarity.Legendary => "#ff8000",
        LootRarity.Epic      => "#a335ee",
        LootRarity.Rare      => "#0070dd",
        LootRarity.Uncommon  => "#1eff00",
        _                    => "#ffffff"
    };

    /// <summary>Builds the welcome panel. CS2's CenterHtml box is a FIXED, small size —
    /// it cannot grow, so this is deliberately compact: a short title, one subtitle line,
    /// and the links condensed so everything fits inside the box without clipping. Long
    /// config values are the enemy here; keep WelcomeSubtitle short.</summary>
    private string BuildWelcomeHtml()
    {
        static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        const string sep = "★★★★★★★★";
        var sb = new System.Text.StringBuilder(384);

        // Title (gold, bold) framed by a short star run.
        sb.Append($"<font class='fontSize-m' color='#FFD700'>{sep}</font><br>");
        sb.Append($"<font class='fontSize-l' color='#FFD700'><b>{Esc(_config.WelcomeServerName)}</b></font><br>");
        sb.Append($"<font class='fontSize-m' color='#FFD700'>{sep}</font><br>");

        // One subtitle line (white) — keep it short in config or it wraps.
        if (!string.IsNullOrWhiteSpace(_config.WelcomeSubtitle))
            sb.Append($"<font class='fontSize-sm' color='#FFFFFF'>{Esc(_config.WelcomeSubtitle)}</font><br>");

        // Links condensed onto single lines, small, colored, bold.
        if (!string.IsNullOrWhiteSpace(_config.WelcomeDiscord))
            sb.Append($"<font class='fontSize-sm' color='#7FBFFF'><b>{Esc(_config.WelcomeDiscord)}</b></font><br>");
        if (!string.IsNullOrWhiteSpace(_config.WelcomeWebsite))
            sb.Append($"<font class='fontSize-sm' color='#7CFC00'><b>{Esc(_config.WelcomeWebsite)}</b></font>");

        return sb.ToString();
    }

}
