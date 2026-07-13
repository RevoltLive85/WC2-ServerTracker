using WC2.API.Models;

namespace WC2.Quests;

public sealed class QuestsFileConfig
{
    public int DailyQuestCount { get; set; } = 3;
    public List<QuestDefinition> Quests { get; set; } = new();

    public static QuestsFileConfig Default() => new()
    {
        Quests =
        {
            new QuestDefinition { Id = "daily_kills_20", DisplayName = "Culling the Weak", Description = "Defeat 20 enemies.",
                Cadence = QuestCadence.Daily, Objective = QuestObjectiveType.Kill, Required = 20, RewardGold = 150, RewardXp = 300 },
            new QuestDefinition { Id = "daily_headshots_5", DisplayName = "Between the Eyes", Description = "Land 5 headshot kills.",
                Cadence = QuestCadence.Daily, Objective = QuestObjectiveType.HeadshotKill, Required = 5, RewardGold = 200, RewardXp = 400 },
            new QuestDefinition { Id = "daily_rounds_5", DisplayName = "Hold the Line", Description = "Win 5 rounds.",
                Cadence = QuestCadence.Daily, Objective = QuestObjectiveType.WinRound, Required = 5, RewardGold = 175, RewardXp = 350 },
            new QuestDefinition { Id = "daily_bossdmg_1500", DisplayName = "Giant Slayer", Description = "Deal 1500 damage to bosses.",
                Cadence = QuestCadence.Daily, Objective = QuestObjectiveType.DealDamageToBoss, Required = 1500, RewardGold = 250, RewardXp = 500 },
            new QuestDefinition { Id = "weekly_boss_3", DisplayName = "Trophy Collector", Description = "Slay 3 world bosses.",
                Cadence = QuestCadence.Weekly, Objective = QuestObjectiveType.KillBoss, Required = 3,
                RewardGold = 800, RewardXp = 2000, RewardShards = 2, RewardTitle = "Boss Hunter" }
        }
    };
}
