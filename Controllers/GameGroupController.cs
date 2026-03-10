using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using webapp_miniproject.Models;

namespace webapp_miniproject.Controllers;

public class GameGroupController : Controller
{
    private readonly Supabase.Client _supabase;

    private static readonly string[] ExtraGameCatalog =
    {
        "Apex Legends",
        "League of Legends",
        "Mobile Legends: Bang Bang",
        "Counter-Strike 2",
        "Overwatch 2",
        "Fortnite",
        "EA FC 26",
        "Marvel Rivals"
    };

    public GameGroupController(Supabase.Client supabase)
    {
        _supabase = supabase;
    }

    private static List<GameInfo> MergeExtraGames(List<GameInfo> gameInfos)
    {
        var existingNames = new HashSet<string>(
            gameInfos.Select(g => g.Name),
            StringComparer.OrdinalIgnoreCase);

        var extraGames = ExtraGameCatalog
            .Where(name => !existingNames.Contains(name))
            .Select((name, index) => new GameInfo
            {
                Id = 100000 + index,
                Name = name,
                GameGroupInfos = new List<GameGroupInfo>()
            });

        gameInfos.AddRange(extraGames);
        return gameInfos.OrderBy(g => g.Name).ToList();
    }

    private async Task<int> ResolveGameIdAsync(int gameId)
    {
        if (gameId < 100000)
        {
            return gameId;
        }

        var index = gameId - 100000;
        if (index < 0 || index >= ExtraGameCatalog.Length)
        {
            return gameId;
        }

        var gameName = ExtraGameCatalog[index];

        var existingResponse = await _supabase
            .From<GameInfo>()
            .Where(g => g.Name == gameName)
            .Get();

        var existing = existingResponse.Models.FirstOrDefault();
        if (existing is not null)
        {
            return existing.Id;
        }

        var created = await _supabase
            .From<GameInfo>()
            .Insert(new GameInfo { Name = gameName });

        return created.Models.First().Id;
    }

    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<List<GameInfo>> FetchGameInfosWithGroups()
    {
        var gamesResponse = await _supabase.From<GameInfo>().Get();
        var gameGroupsResponse = await _supabase.From<GameGroupInfo>().Get();

        var groups = gameGroupsResponse.Models;

        foreach (var game in gamesResponse.Models)
        {
            game.GameGroupInfos = groups
                .Where(g => g.GameId == game.Id)
                .ToList();

            foreach (var group in game.GameGroupInfos)
            {
                group.ParseMetaFromDescription();
            }
        }

        return gamesResponse.Models.OrderBy(g => g.Name).ToList();
    }

    // -- Index ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var gameInfos = await FetchGameInfosWithGroups();
        var gameCatalog = MergeExtraGames(gameInfos);
        return View(gameCatalog);
    }

    [HttpGet]
    public async Task<IActionResult> IndexPartial(string? q, int? gameId, string? queueType, string? rank)
    {
        var gameInfos = await FetchGameInfosWithGroups();

        foreach (var gameInfo in gameInfos)
        {
            gameInfo.GameGroupInfos = gameInfo.GameGroupInfos
                .Where(g =>
                    (gameId == null || g.GameId == gameId) &&
                    (
                        string.IsNullOrWhiteSpace(queueType) ||
                        g.QueueType.Equals(queueType, StringComparison.OrdinalIgnoreCase)
                    ) &&
                    (
                        string.IsNullOrWhiteSpace(rank) ||
                        (g.RankTier?.Contains(rank, StringComparison.OrdinalIgnoreCase) ?? false)
                    ) &&
                    (
                        string.IsNullOrWhiteSpace(q) ||
                        (g.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (g.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    )
                )
                .ToList();
        }

        var filtered = gameInfos.Where(g => g.GameGroupInfos.Any()).ToList();
        return PartialView("_GameGroups", filtered);
    }

    // -- Details ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        gameGroup.ParseMetaFromDescription();

        var gameResponse = await _supabase
            .From<GameInfo>()
            .Where(g => g.Id == gameGroup.GameId)
            .Get();

        gameGroup.Game = gameResponse.Models.FirstOrDefault();

        ViewBag.IsOwner = CurrentUserId is not null && CurrentUserId == gameGroup.CreatedBy;
        return View(gameGroup);
    }

    // -- Create Game Group ----------------------------------------------------------------

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var gamesResponse = await _supabase.From<GameInfo>().Get();
        ViewBag.Games = MergeExtraGames(gamesResponse.Models);
        return View();
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(GameGroupInfo model)
    {
        var resolvedGameId = await ResolveGameIdAsync(model.GameId);

        // Let Supabase generates ID
        var gameGroup = new GameGroupInfo
        {
            GameId = resolvedGameId,
            Title = model.Title,
            Description = model.Description,
            ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CurrentUserId,
            QueueType = model.QueueType,
            RankTier = model.RankTier
        };

        gameGroup.ApplyMetaToDescription();

        await _supabase.From<GameGroupInfo>().Insert(gameGroup);
        return RedirectToAction(nameof(Index));
    }

    // -- Edit Game Group ----------------------------------------------------------------

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();
        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        if (gameGroup.CreatedBy != CurrentUserId) return Forbid();
        gameGroup.ParseMetaFromDescription();

        var gamesResponse = await _supabase.From<GameInfo>().Get();
        ViewBag.Games = MergeExtraGames(gamesResponse.Models);

        return View(gameGroup);
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Edit(int id, GameGroupInfo model)
    {
        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();
        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        if (gameGroup.CreatedBy != CurrentUserId) return Forbid();

        gameGroup.GameId = await ResolveGameIdAsync(model.GameId);
        gameGroup.Title = model.Title;
        gameGroup.Description = model.Description;
        gameGroup.ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl;
        gameGroup.QueueType = model.QueueType;
        gameGroup.RankTier = model.RankTier;
        gameGroup.ApplyMetaToDescription();

        await _supabase.From<GameGroupInfo>().Update(gameGroup);
        return RedirectToAction(nameof(Details), new { id });
    }

    // -- Delete Game Group ----------------------------------------------------------------

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();
        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        if (gameGroup.CreatedBy != CurrentUserId) return Forbid();

        await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Delete();

        return RedirectToAction(nameof(Index));
    }
}