using WC2.API.Models;

namespace WC2.API.Interfaces;

public interface IQuestService
{
    IReadOnlyList<QuestProgressSnapshot> GetActiveQuests(ulong steamId);
    /// <summary>Advances every active quest whose objective matches the given type/target.</summary>
    void ReportObjective(ulong steamId, QuestObjectiveType type, string? target = null, int amount = 1);
    void ReloadDefinitions();
}
