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

    private async Task<List<GroupJoinRequestInfo>> FetchJoinRequestsSafeAsync()
    {
        try
        {
            var response = await _supabase.From<GroupJoinRequestInfo>().Get();
            return response.Models;
        }
        catch
        {
            return new List<GroupJoinRequestInfo>();
        }
    }

    private static void ApplyJoinStats(IEnumerable<GameGroupInfo> groups, IEnumerable<GroupJoinRequestInfo> requests)
    {
        var requestLookup = requests
            .GroupBy(r => r.GroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var group in groups)
        {
            var groupRequests = requestLookup.TryGetValue(group.Id, out var values)
                ? values
                : new List<GroupJoinRequestInfo>();

            var ownerCount = group.CreatedBy.HasValue ? 1 : 0;
            group.CurrentMemberCount = ownerCount + groupRequests.Count(r => r.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase));
            group.PendingRequestCount = groupRequests.Count(r => r.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task AttachApplicantNamesAsync(List<GroupJoinRequestInfo> requests)
    {
        if (!requests.Any())
        {
            return;
        }

        var users = await _supabase.From<UserInfo>().Get();
        var lookup = users.Models.ToDictionary(u => u.Id, u => u.Username);

        foreach (var request in requests)
        {
            request.Username = lookup.TryGetValue(request.UserId, out var username)
                ? username
                : $"User {request.UserId}";
        }
    }

    private static void SortGameGroups(List<GameInfo> gameInfos, string? sort)
    {
        foreach (var gameInfo in gameInfos)
        {
            gameInfo.GameGroupInfos = (sort ?? "newest").ToLowerInvariant() switch
            {
                "oldest" => gameInfo.GameGroupInfos.OrderBy(g => g.CreatedAt).ToList(),
                "members" => gameInfo.GameGroupInfos
                    .OrderByDescending(g => g.MaxMembers - g.CurrentMemberCount)
                    .ThenByDescending(g => g.CreatedAt)
                    .ToList(),
                _ => gameInfo.GameGroupInfos.OrderByDescending(g => g.CreatedAt).ToList(),
            };
        }
    }

    private async Task<List<GameInfo>> FetchGameInfosWithGroups()
    {
        var gamesResponse = await _supabase.From<GameInfo>().Get();
        var gameGroupsResponse = await _supabase.From<GameGroupInfo>().Get();
        var joinRequests = await FetchJoinRequestsSafeAsync();

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

        ApplyJoinStats(gamesResponse.Models.SelectMany(g => g.GameGroupInfos), joinRequests);

        return gamesResponse.Models.OrderBy(g => g.Name).ToList();
    }

    // -- Index ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var gameInfos = await FetchGameInfosWithGroups();
        SortGameGroups(gameInfos, "newest");
        var gameCatalog = MergeExtraGames(gameInfos);
        return View(gameCatalog);
    }

    [HttpGet]
    public async Task<IActionResult> IndexPartial(string? q, int? gameId, string? queueType, string? rank, string? status, string? sort)
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
                        string.IsNullOrWhiteSpace(status) ||
                        g.EffectiveStatus.Equals(status, StringComparison.OrdinalIgnoreCase)
                    ) &&
                    (
                        string.IsNullOrWhiteSpace(q) ||
                        (g.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (g.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    )
                )
                .ToList();
        }

            SortGameGroups(gameInfos, sort);

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

        var joinRequests = await FetchJoinRequestsSafeAsync();
        var groupRequests = joinRequests.Where(r => r.GroupId == gameGroup.Id).ToList();
        ApplyJoinStats(new[] { gameGroup }, groupRequests);

        var currentUserId = CurrentUserId;
        var latestCurrentUserRequest = currentUserId is null
            ? null
            : groupRequests
                .Where(r => r.UserId == currentUserId.Value)
                .OrderByDescending(r => r.UpdatedAt)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();

        gameGroup.CurrentUserJoinStatus = latestCurrentUserRequest?.Status;
        ViewBag.CurrentUserJoinStatus = latestCurrentUserRequest?.Status;
        ViewBag.IsOwner = currentUserId is not null && currentUserId == gameGroup.CreatedBy;

        if ((bool)ViewBag.IsOwner)
        {
            var pendingRequests = groupRequests
                .Where(r => r.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.CreatedAt)
                .ToList();

            await AttachApplicantNamesAsync(pendingRequests);
            ViewBag.PendingJoinRequests = pendingRequests;
        }

        return View(gameGroup);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestJoin(int id)
    {
        var currentUserId = CurrentUserId;
        if (currentUserId is null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });
        }

        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null)
        {
            return NotFound();
        }

        gameGroup.ParseMetaFromDescription();

        if (gameGroup.CreatedBy == currentUserId.Value)
        {
            TempData["GroupActionError"] = "You already own this group.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var joinRequests = await FetchJoinRequestsSafeAsync();
        var groupRequests = joinRequests.Where(r => r.GroupId == id).ToList();
        ApplyJoinStats(new[] { gameGroup }, groupRequests);

        if (!gameGroup.EffectiveStatus.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            TempData["GroupActionError"] = $"This group is {gameGroup.EffectiveStatus.ToLowerInvariant()} right now.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var existing = groupRequests
            .Where(r => r.UserId == currentUserId.Value)
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        try
        {
            if (existing is null)
            {
                await _supabase
                    .From<GroupJoinRequestInfo>()
                    .Insert(new GroupJoinRequestInfo
                    {
                        GroupId = id,
                        UserId = currentUserId.Value,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
            }
            else if (existing.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                TempData["GroupActionMessage"] = "Your join request is already pending.";
                return RedirectToAction(nameof(Details), new { id });
            }
            else if (existing.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                TempData["GroupActionMessage"] = "You are already in this group.";
                return RedirectToAction(nameof(Details), new { id });
            }
            else
            {
                existing.Status = "Pending";
                existing.UpdatedAt = DateTime.UtcNow;
                await _supabase.From<GroupJoinRequestInfo>().Update(existing);
            }

            TempData["GroupActionMessage"] = "Join request sent.";
        }
        catch
        {
            TempData["GroupActionError"] = "Join requests are not ready yet. Create the GroupJoinRequest table in Supabase first.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateJoinRequest(int id, int requestId, string decision)
    {
        var currentUserId = CurrentUserId;
        if (currentUserId is null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });
        }

        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null)
        {
            return NotFound();
        }

        if (gameGroup.CreatedBy != currentUserId.Value)
        {
            return Forbid();
        }

        gameGroup.ParseMetaFromDescription();

        var joinRequests = await FetchJoinRequestsSafeAsync();
        var groupRequests = joinRequests.Where(r => r.GroupId == id).ToList();
        var request = groupRequests.FirstOrDefault(r => r.Id == requestId);

        if (request is null)
        {
            TempData["GroupActionError"] = "Join request not found.";
            return RedirectToAction(nameof(Details), new { id });
        }

        ApplyJoinStats(new[] { gameGroup }, groupRequests);

        if (decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
            && !request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            && gameGroup.CurrentMemberCount >= gameGroup.MaxMembers)
        {
            TempData["GroupActionError"] = "This group is already full.";
            return RedirectToAction(nameof(Details), new { id });
        }

        request.Status = decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : "Rejected";
        request.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _supabase.From<GroupJoinRequestInfo>().Update(request);
            TempData["GroupActionMessage"] = request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                ? "Request approved."
                : "Request rejected.";
        }
        catch
        {
            TempData["GroupActionError"] = "Could not update this join request.";
        }

        return RedirectToAction(nameof(Details), new { id });
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
        // var resolvedGameId = await ResolveGameIdAsync(model.GameId);

        // Let Supabase generates ID
        var gameGroup = new GameGroupInfo
        {
            // GameId = resolvedGameId,
            Title = model.Title,
            Description = model.Description,
            ImageUrl = string.IsNullOrWhiteSpace(model.ImageUrl) ? null : model.ImageUrl,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CurrentUserId,
            QueueType = model.QueueType,
            RankTier = model.RankTier,
            MaxMembers = model.MaxMembers,
            RecruitmentStatus = model.RecruitmentStatus
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
        gameGroup.MaxMembers = model.MaxMembers;
        gameGroup.RecruitmentStatus = model.RecruitmentStatus;
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