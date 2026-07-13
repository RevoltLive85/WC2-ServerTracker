namespace WC2.Quests.Models;

/// <summary>Persisted per player. Daily set is deterministic per (steamId, date) so
/// there is nothing to sync — reconnects always resolve the same quests.</summary>
public sealed class PlayerQuestState
{
    public string DailyDateKey { get; set; } = "";
    public string WeeklyDateKey { get; set; } = "";
    public Dictionary<string, int> Progress { get; set; } = new();
    public HashSet<string> Completed { get; set; } = new();
}
