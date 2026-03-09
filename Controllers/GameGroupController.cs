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


// ทดสอบ API สำหรับดึงกลุ่มเกมตามเกม
    [HttpGet]
    public IActionResult GetByGame(int gameId)
    {
        var groups = new List<GameGroupInfo>
    {
        // ROV
        new GameGroupInfo{ Id=1, GameId=1, Title="nep 🎀", Description="RoV เล่นซัพ", ImageUrl="https://picsum.photos/300/400?1"},
        new GameGroupInfo{ Id=2, GameId=1, Title="xbaifern", Description="RoV เมจ", ImageUrl="https://picsum.photos/300/400?2"},
        new GameGroupInfo{ Id=3, GameId=1, Title="TankMain", Description="RoV แทงค์สายเปิด", ImageUrl="https://picsum.photos/300/400?3"},
        new GameGroupInfo{ Id=4, GameId=1, Title="CarryOnly", Description="RoV แครี่ยิงแรง", ImageUrl="https://picsum.photos/300/400?4"},
        new GameGroupInfo{ Id=5, GameId=1, Title="JungleKing", Description="RoV ป่าโหด", ImageUrl="https://picsum.photos/300/400?5"},

        // PUBG
        new GameGroupInfo{ Id=6, GameId=2, Title="PUBG Sniper", Description="ชอบสไนเปอร์", ImageUrl="https://picsum.photos/300/400?6"},
        new GameGroupInfo{ Id=7, GameId=2, Title="Rush Squad", Description="เล่นดันบ้าน", ImageUrl="https://picsum.photos/300/400?7"},
        new GameGroupInfo{ Id=8, GameId=2, Title="Conqueror Rank", Description="หาเพื่อนตีแรงค์", ImageUrl="https://picsum.photos/300/400?8"},
        new GameGroupInfo{ Id=9, GameId=2, Title="Chill Player", Description="เล่นชิลๆ", ImageUrl="https://picsum.photos/300/400?9"},

        // Free Fire
        new GameGroupInfo{ Id=10, GameId=3, Title="FF Pro", Description="Carry Rank", ImageUrl="https://picsum.photos/300/400?10"},
        new GameGroupInfo{ Id=11, GameId=3, Title="Shotgun Master", Description="สายปืนลูกซอง", ImageUrl="https://picsum.photos/300/400?11"},
        new GameGroupInfo{ Id=12, GameId=3, Title="Rank Push", Description="หาเพื่อนดันแรงค์", ImageUrl="https://picsum.photos/300/400?12"},

        // CODM
        new GameGroupInfo{ Id=13, GameId=4, Title="COD Sniper", Description="Sniper only", ImageUrl="https://picsum.photos/300/400?13"},
        new GameGroupInfo{ Id=14, GameId=4, Title="MP Ranked", Description="หาเพื่อน MP Rank", ImageUrl="https://picsum.photos/300/400?14"},

        // Valorant
        new GameGroupInfo{ Id=15, GameId=5, Title="Valorant Duelist", Description="Main Jett", ImageUrl="https://picsum.photos/300/400?15"},
        new GameGroupInfo{ Id=16, GameId=5, Title="Immortal Push", Description="Rank Immortal", ImageUrl="https://picsum.photos/300/400?16"},
        new GameGroupInfo{ Id=17, GameId=5, Title="Chill Team", Description="เล่นชิล ไม่ toxic", ImageUrl="https://picsum.photos/300/400?17"}
    };

        var result = groups
            .Where(x => x.GameId == gameId)
            .Select(g => new
            {
                id = g.Id,
                gameId = g.GameId,
                title = g.Title,
                description = g.Description,
                imageUrl = g.ImageUrl
            });

        return Json(result);
    }

}
