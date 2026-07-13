namespace WC2.API.Models;

public sealed class RegionDefinition
{
    public required string Id { get; init; }           // "frozen_north"
    public required string DisplayName { get; init; }  // "The Frozen North"
    public string Flavor { get; init; } = "";          // banner subtitle
    public string ColorHex { get; init; } = "#7fd4ff";
    public int Difficulty { get; init; } = 1;          // 1..5, drives XP/gold region bonus
    public int RecommendedPlayers { get; init; } = 8;
    public float XpBonus { get; init; } = 0f;          // additive, e.g. 0.15 = +15%
    public float GoldBonus { get; init; } = 0f;
    public string? RegionBossId { get; init; }
    public List<string> Maps { get; init; } = new();   // workshop/official map names
    public List<string> AmbientLines { get; init; } = new(); // periodic atmosphere chat
}
