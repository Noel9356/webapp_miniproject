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

    [Column("queue_type")]
    public string QueueType { get; set; } = "Casual";

    [Column("rank_tier")]
    public string? RankTier { get; set; }

    [Column("max_members")]
    public int MaxMembers { get; set; } = 5;

    [Column("recruitment_status")]
    public string RecruitmentStatus { get; set; } = "Open";

    [JsonIgnore]
    public int CurrentMemberCount { get; set; }

    [JsonIgnore]
    public int PendingRequestCount { get; set; }

    [JsonIgnore]
    public string? CurrentUserJoinStatus { get; set; }

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

    [JsonIgnore]
    public string EffectiveStatus
    {
        get
        {
            if (RecruitmentStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            {
                return "Closed";
            }

            return CurrentMemberCount >= MaxMembers ? "Full" : "Open";
        }
    }

    public void ParseMetaFromDescription()
    {
        QueueType = "Casual";
        RankTier = null;
        MaxMembers = 5;
        RecruitmentStatus = "Open";
        PlainDescription = Description?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(Description) || !Description.StartsWith("[", StringComparison.OrdinalIgnoreCase))
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

            if (pair[0].Equals("capacity", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pair[1], out var capacity)
                && capacity > 0)
            {
                MaxMembers = capacity;
            }

            if (pair[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                RecruitmentStatus = pair[1].Equals("Closed", StringComparison.OrdinalIgnoreCase)
                    ? "Closed"
                    : "Open";
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
        var safeStatus = RecruitmentStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase)
            ? "Closed"
            : "Open";
        var safeCapacity = MaxMembers <= 0 ? 5 : MaxMembers;

        var cleanDescription = (Description ?? string.Empty).Trim();
        var cleanRank = string.IsNullOrWhiteSpace(RankTier) ? null : RankTier.Trim();

        var metaParts = new List<string>
        {
            $"mode={safeMode}",
            $"capacity={safeCapacity}",
            $"status={safeStatus}"
        };

        if (safeMode == "Competitive" && !string.IsNullOrWhiteSpace(cleanRank))
        {
            metaParts.Add($"rank={cleanRank}");
        }

        var meta = $"[{string.Join(';', metaParts)}]";

        Description = string.IsNullOrWhiteSpace(cleanDescription)
            ? meta
            : $"{meta}\n{cleanDescription}";
    }
}