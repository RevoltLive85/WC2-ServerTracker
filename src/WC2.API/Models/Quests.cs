namespace WC2.API.Models;

public enum QuestObjectiveType { Kill, HeadshotKill, KillBoss, WinRound, PlayMap, DealDamageToBoss, ParticipateEvent }
public enum QuestCadence { Daily, Weekly, Achievement }

public sealed class QuestDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public QuestCadence Cadence { get; init; } = QuestCadence.Daily;
    public QuestObjectiveType Objective { get; init; }
    public string? Target { get; init; }            // e.g. boss id, map name; null = any
    public int Required { get; init; } = 10;
    public long RewardGold { get; init; }
    public int RewardXp { get; init; }
    public long RewardShards { get; init; }
    public string? RewardTitle { get; init; }
}

public sealed record QuestProgressSnapshot(QuestDefinition Definition, int Progress, bool Completed);
