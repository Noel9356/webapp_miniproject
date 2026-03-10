using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace webapp_miniproject.Models;

[Table("GameGroupDb")]
public class GameGroupInfo : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("game_id")]
    public int GameId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [JsonIgnore]
    public string QueueType { get; set; } = "Casual";

    [JsonIgnore]
    public string? RankTier { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("created_by")]
    public int? CreatedBy { get; set; }

    // JsonIgnore tells the Supabase SDK to skip this property on INSERT/UPDATE
    [JsonIgnore]
    public GameInfo? Game { get; set; }

    [JsonIgnore]
    public string PlainDescription { get; set; } = "";

    public void ParseMetaFromDescription()
    {
        QueueType = "Casual";
        RankTier = null;
        PlainDescription = Description?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(Description) || !Description.StartsWith("[mode=", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var endIndex = Description.IndexOf(']');
        if (endIndex <= 1)
        {
            return;
        }

        var header = Description.Substring(1, endIndex - 1);
        var metaParts = header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in metaParts)
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0].Equals("mode", StringComparison.OrdinalIgnoreCase))
            {
                QueueType = pair[1].Equals("Competitive", StringComparison.OrdinalIgnoreCase)
                    ? "Competitive"
                    : "Casual";
            }

            if (pair[0].Equals("rank", StringComparison.OrdinalIgnoreCase))
            {
                RankTier = string.IsNullOrWhiteSpace(pair[1]) ? null : pair[1];
            }
        }

        PlainDescription = Description[(endIndex + 1)..].Trim();
        Description = PlainDescription;
    }

    public void ApplyMetaToDescription()
    {
        var safeMode = QueueType.Equals("Competitive", StringComparison.OrdinalIgnoreCase)
            ? "Competitive"
            : "Casual";

        var cleanDescription = (Description ?? string.Empty).Trim();
        var cleanRank = string.IsNullOrWhiteSpace(RankTier) ? null : RankTier.Trim();

        var meta = safeMode == "Competitive" && !string.IsNullOrWhiteSpace(cleanRank)
            ? $"[mode={safeMode};rank={cleanRank}]"
            : $"[mode={safeMode}]";

        Description = string.IsNullOrWhiteSpace(cleanDescription)
            ? meta
            : $"{meta}\n{cleanDescription}";
    }
}