using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using webapp_miniproject.Models;

namespace webapp_miniproject.Controllers;

public class GameGroupController : Controller
{
    private readonly Supabase.Client _supabase;

    public GameGroupController(Supabase.Client supabase)
    {
        _supabase = supabase;
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
        }

        return gamesResponse.Models.OrderBy(g => g.Name).ToList();
    }

    // -- Index ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var gameInfos = await FetchGameInfosWithGroups();
        return View(gameInfos);
    }

    [HttpGet]
    public async Task<IActionResult> IndexPartial(string? q, int? gameId)
    {
        var gameInfos = await FetchGameInfosWithGroups();

        foreach (var gameInfo in gameInfos)
        {
            gameInfo.GameGroupInfos = gameInfo.GameGroupInfos
                .Where(g =>
                    (gameId == null || g.GameId == gameId) &&
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
        ViewBag.Games = gamesResponse.Models.OrderBy(g => g.Name).ToList();
        return View();
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(GameGroupInfo model)
    {
        // Let Supabase generates ID
        var gameGroup = new GameGroupInfo
        {
            GameId = model.GameId,
            Title = model.Title,
            Description = model.Description,
            ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CurrentUserId
        };

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

        var gamesResponse = await _supabase.From<GameInfo>().Get();
        ViewBag.Games = gamesResponse.Models.OrderBy(g => g.Name).ToList();

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

        gameGroup.GameId = model.GameId;
        gameGroup.Title = model.Title;
        gameGroup.Description = model.Description;
        gameGroup.ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl;

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