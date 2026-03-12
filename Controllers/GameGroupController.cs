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

    private int? CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // -- Helper ----------------------------------------------------------------

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

    // Case-insensitive status check
    private static bool StatusIs(string status, string expected) =>
        status.Equals(expected, StringComparison.OrdinalIgnoreCase);

    // -- Index ----------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var gameInfos = await FetchGameInfosWithGroups();
        SortGameGroups(gameInfos, "newest");
        return View(gameInfos);
    }

    [HttpGet]
    public async Task<IActionResult> IndexPartial(
        string? q, int? gameId, string? queueType, string? rank, string? status, string? sort)
    {
        var gameInfos = await FetchGameInfosWithGroups();

        foreach (var gameInfo in gameInfos)
        {
            gameInfo.GameGroupInfos = gameInfo.GameGroupInfos
                .Where(g =>
                    (gameId    == null || g.GameId == gameId) &&
                    (string.IsNullOrWhiteSpace(queueType) || StatusIs(g.QueueType, queueType)) &&
                    (string.IsNullOrWhiteSpace(rank)      || (g.RankTier?.Contains(rank, StringComparison.OrdinalIgnoreCase) ?? false)) &&
                    (string.IsNullOrWhiteSpace(status)    || StatusIs(g.EffectiveStatus, status)) &&
                    (string.IsNullOrWhiteSpace(q)         ||
                        (g.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (g.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
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
        // Fetch the group and its associated game
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

        // Fetch all requests for this group, then split by status
        var allRequests = await FetchJoinRequestsSafeAsync();
        var groupRequests = allRequests.Where(r => r.GroupId == id).ToList();
        var approvedRequests = groupRequests
            .Where(r => r.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pendingRequests = groupRequests
            .Where(r => r.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Resolve usernames for all approved members in one query
        var allUsers = (await _supabase.From<UserInfo>().Get()).Models;
        var approvedUserIds = approvedRequests.Select(r => r.UserId).ToHashSet();
        var members = allUsers.Where(u => approvedUserIds.Contains(u.Id)).ToList();

        // Find the current user's most recent request for this group (any status)
        var myRequest = CurrentUserId is not null
            ? groupRequests
                .Where(r => r.UserId == CurrentUserId.Value)
                .OrderByDescending(r => r.UpdatedAt)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault()
            : null;

        // For owner: pair each pending request with the requester's username
        List<(GroupJoinRequestInfo Request, string Username)> pendingWithUsers = new();
        List<(GroupJoinRequestInfo Request, string Username)> approvedWithUsers = new();
        if (CurrentUserId == gameGroup.CreatedBy)
        {
            var pendingUserIds = pendingRequests.Select(r => r.UserId).ToHashSet();
            var pendingUsers = allUsers.Where(u => pendingUserIds.Contains(u.Id))
                .ToDictionary(u => u.Id);

            pendingWithUsers = pendingRequests
                .Select(r => (r, pendingUsers.TryGetValue(r.UserId, out var u) ? u.Username : $"User #{r.UserId}"))
                .ToList();

            var approvedUsers = allUsers
                .Where(u => approvedUserIds.Contains(u.Id))
                .ToDictionary(u => u.Id);

            approvedWithUsers = approvedRequests
                .Select(r => (r, approvedUsers.TryGetValue(r.UserId, out var u) ? u.Username : $"User #{r.UserId}"))
                .ToList();
        }

        ViewBag.IsOwner = CurrentUserId is not null && gameGroup.CreatedBy == CurrentUserId;
        ViewBag.Members = members;
        ViewBag.MemberCount = members.Count;
        ViewBag.PendingCount = pendingRequests.Count;
        ViewBag.myRequest = myRequest;
        ViewBag.PendingRequests = pendingWithUsers;
        ViewBag.ApprovedMembers = approvedWithUsers;
        ViewBag.Owner = allUsers.FirstOrDefault(u => u.Id == gameGroup.CreatedBy);

        return View(gameGroup);
    }

    // -- Request Join ----------------------------------------------------------------

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestJoin(int id)
    {
        if (CurrentUserId is null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });
        }

        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();

        gameGroup.ParseMetaFromDescription();

        // Owners can't request to join their own group
        if (gameGroup.CreatedBy == CurrentUserId)
        {
            TempData["Error"] = "You already own this group.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Get this user's most recent request for this group
        var allRequests = await FetchJoinRequestsSafeAsync();
        var myRequest = allRequests
            .Where(r => r.GroupId == id && r.UserId == CurrentUserId.Value)
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        try
        {
            if (myRequest is null)
            {
                // No prior request — create a fresh one
                await _supabase
                    .From<GroupJoinRequestInfo>()
                    .Insert(new GroupJoinRequestInfo
                    {
                        GroupId = id,
                        UserId = CurrentUserId.Value,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });
                TempData["Message"] = "Join request sent.";
            }
            else if (myRequest.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "Your request is already pending.";
            }
            else if (myRequest.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "You are already a member.";
            }
            else
            {
                // Was Rejected, Canceled, or Left — reuse the row and set back to Pending
                await _supabase
                    .From<GroupJoinRequestInfo>()
                    .Where(r => r.Id == myRequest.Id)
                    .Set(r => r.Status, "Pending")
                    .Set(r => r.UpdatedAt, DateTime.UtcNow)
                    .Update();
                TempData["Message"] = "Join request re-sent.";
            }
        }
        catch
        {
            TempData["Error"] = "Join requests are not ready yet. Create the GroupJoinRequest table in Supabase first.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // -- Cancel Request ----------------------------------------------------------------

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CancelRequest(int id)
    {
        var allRequests = await FetchJoinRequestsSafeAsync();
        var myRequest = allRequests
            .FirstOrDefault(r => r.GroupId == id && r.UserId == CurrentUserId!.Value && r.Status == "Pending");

        if (myRequest is not null)
        {
            await _supabase
                .From<GroupJoinRequestInfo>()
                .Where(r => r.Id == myRequest.Id)
                .Set(r => r.Status, "Canceled")
                .Set(r => r.UpdatedAt, DateTime.UtcNow)
                .Update();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // -- Leave Group ----------------------------------------------------------------

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Leave(int id)
    {
        var allRequests = await FetchJoinRequestsSafeAsync();
        var myRequest = allRequests
            .FirstOrDefault(r => r.GroupId == id && r.UserId == CurrentUserId!.Value && r.Status == "Approved");

        if (myRequest is not null)
        {
            await _supabase
                .From<GroupJoinRequestInfo>()
                .Where(r => r.Id == myRequest.Id)
                .Set(r => r.Status, "Left")
                .Set(r => r.UpdatedAt, DateTime.UtcNow)
                .Update();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KickMember(int id, int requestId)
    {
        if (CurrentUserId is null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });
        }

        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        if (gameGroup.CreatedBy != CurrentUserId) return Forbid();

        var allRequests = await FetchJoinRequestsSafeAsync();
        var approvedRequest = allRequests.FirstOrDefault(r =>
            r.Id == requestId
            && r.GroupId == id
            && r.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase));

        if (approvedRequest is null)
        {
            TempData["Error"] = "Member not found or is no longer active in this group.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _supabase
                .From<GroupJoinRequestInfo>()
                .Where(r => r.Id == approvedRequest.Id)
                .Set(r => r.Status, "Removed")
                .Set(r => r.UpdatedAt, DateTime.UtcNow)
                .Update();

            TempData["Message"] = "Member removed from the group.";
        }
        catch
        {
            TempData["Error"] = "Could not remove this member right now.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // -- Update Join Request (owner approve/reject) ----------------------------------------------------------------

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateJoinRequest(int id, int requestId, string decision)
    {
        if (CurrentUserId is null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action(nameof(Details), new { id }) });
        }

        var groupResponse = await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Get();

        var gameGroup = groupResponse.Models.FirstOrDefault();
        if (gameGroup is null) return NotFound();
        if (gameGroup.CreatedBy != CurrentUserId) return Forbid();

        gameGroup.ParseMetaFromDescription();

        var allRequests = await FetchJoinRequestsSafeAsync();
        var request = allRequests.FirstOrDefault(r => r.Id == requestId);
        if (request is null)
        {
            TempData["Error"] = "Request not found.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
            && !request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            && gameGroup.CurrentMemberCount >= gameGroup.MaxMembers)
        {
            TempData["Error"] = "This group is already full.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var newStatus = decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
                ? "Approved"
                : "Rejected";

            await _supabase
                .From<GroupJoinRequestInfo>()
                .Where(r => r.Id == requestId)
                .Set(r => r.Status, newStatus)
                .Set(r => r.UpdatedAt, DateTime.UtcNow)
                .Update();

            TempData["Message"] = newStatus == "Approved"
                ? "Request approved."
                : "Request rejected.";
        }
        catch
        {
            TempData["Error"] = "Could not update this join request.";
        }
    
        return RedirectToAction(nameof(Details), new { id });
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
            .From<GroupJoinRequestInfo>()
            .Where(r => r.GroupId == id)
            .Delete();

        await _supabase
            .From<GameGroupInfo>()
            .Where(g => g.Id == id)
            .Delete();

        return RedirectToAction(nameof(Index));
    }
}