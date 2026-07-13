using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using WC2.API.Capabilities;
using WC2.API.Events;
using WC2.API.Interfaces;
using WC2.API.Models;
using WC2.Quests.Services;
using WC2.Shared;
using WC2.Shared.Configuration;
using WC2.Shared.Extensions;

namespace WC2.Quests;

[MinimumApiVersion(200)]
public sealed class WC2QuestsPlugin : BasePlugin
{
    public override string ModuleName => "WC2.Quests";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "ServerTracker.live";
    public override string ModuleDescription => "Daily/weekly quests and achievements with rewards.";

    private JsonConfigStore _store = null!;
    private QuestService _quests = null!;
    private IWc2EventBus _bus = null!;

    public override void Load(bool hotReload)
    {
        _store = new JsonConfigStore(ModuleDirectory, Logger);
        var config = _store.LoadOrCreate("quests.json", QuestsFileConfig.Default);

        _bus = Wc2Bootstrap.EnsureCore(Logger);
        _quests = new QuestService(ModuleDirectory, config, _bus, Logger);
        Capabilities.RegisterPluginCapability(Wc2Capabilities.Quests, () => _quests);

        // ── Objective feeds ──
        RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            var a = ev.Attacker;
            if (!a.IsRealPlayer() || a == ev.Userid) return HookResult.Continue;
            _quests.ReportObjective(a!.SteamID, QuestObjectiveType.Kill);
            if (ev.Headshot) _quests.ReportObjective(a.SteamID, QuestObjectiveType.HeadshotKill);
            return HookResult.Continue;
        });

        RegisterEventHandler<EventRoundEnd>((ev, _) =>
        {
            foreach (var p in PlayerExtensions.RealPlayers())
                if (p.TeamNum == ev.Winner)
                    _quests.ReportObjective(p.SteamID, QuestObjectiveType.WinRound);
            _quests.Flush();
            return HookResult.Continue;
        });

        _bus.Subscribe<BossDamagedEvent>(e =>
            _quests.ReportObjective(e.AttackerSteamId, QuestObjectiveType.DealDamageToBoss, e.Boss.BossId, (int)e.Damage));
        _bus.Subscribe<BossKilledEvent>(e =>
        {
            foreach (var steamId in e.DamageBySteamId.Keys)
                _quests.ReportObjective(steamId, QuestObjectiveType.KillBoss, e.Boss.BossId);
        });

        // ── Reward delivery on completion (economy + warcraft XP via capabilities) ──
        _bus.Subscribe<QuestCompletedEvent>(e =>
        {
            var eco = Wc2Capabilities.Economy.GetOrNull();
            if (e.Quest.RewardGold > 0)   eco?.Grant(e.SteamId, CurrencyType.Gold, e.Quest.RewardGold, $"quest:{e.Quest.Id}");
            if (e.Quest.RewardShards > 0) eco?.Grant(e.SteamId, CurrencyType.WorldstoneShard, e.Quest.RewardShards, $"quest:{e.Quest.Id}");
            if (e.Quest.RewardXp > 0)     Wc2Capabilities.Warcraft.GetOrNull()?.GrantXp(e.SteamId, e.Quest.RewardXp, $"quest:{e.Quest.Id}");
        });

        AddCommand("css_quests", "Show your quest log", (player, cmd) =>
        {
            if (player is null) return;
            cmd.ReplyToCommand("[WC2] ── Quest Log ──");
            foreach (var q in _quests.GetActiveQuests(player.SteamID))
                cmd.ReplyToCommand($"[WC2] {(q.Completed ? "✔" : "•")} {q.Definition.DisplayName} " +
                                   $"({Math.Min(q.Progress, q.Definition.Required)}/{q.Definition.Required}) — {q.Definition.Description}");
        });

        AddCommand("css_wc_reload_quests", "Reload quests.json", (_, cmd) =>
        {
            _quests.ApplyConfig(_store.LoadOrCreate("quests.json", QuestsFileConfig.Default));
            cmd.ReplyToCommand("[WC2] quests.json reloaded.");
        });

        AddTimer(60f, _quests.Flush, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        Logger.LogInformation("[WC2] Quests module loaded with {Count} definitions.", config.Quests.Count);
    }

    public override void Unload(bool hotReload) => _quests.Flush();

}
