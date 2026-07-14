using System.Text.Json.Serialization;

namespace DungeonTracker.Models;

public sealed class QuestCatalogEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Quest tier in the catalog: Heroic, Epic, Legendary, etc.</summary>
    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("soloXP")]
    public int? SoloXp { get; set; }

    [JsonPropertyName("normalXP")]
    public int? NormalXp { get; set; }

    [JsonPropertyName("hardXP")]
    public int? HardXp { get; set; }

    [JsonPropertyName("eliteXP")]
    public int? EliteXp { get; set; }
}
