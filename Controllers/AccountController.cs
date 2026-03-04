using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using webapp_miniproject.Models;

namespace webapp_miniproject.Controllers;

public class AccountController : Controller
{
    private readonly Supabase.Client _supabase;

    public AccountController(Supabase.Client supabase)
    {
        _supabase = supabase;
    }

    // -- Login ----------------------------------------------------------------

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var response = await _supabase
            .From<UserInfo>()
            .Where(u => u.Username == username && u.Password == password)
            .Get();
    
        var user = response.Models.FirstOrDefault();

        // No user matched
        if (user is null)
        {
            ViewBag.Error = "Incorrect username or password.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        await SignInCookie(user.Id.ToString(), user.Username);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    // -- Sign up ----------------------------------------------------------------

    [HttpGet]
    public IActionResult SignUp() => View();

    [HttpPost]
    public async Task<IActionResult> SignUp(string username, string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            ViewBag.Error = "Username is required.";
            return View();
        }

        if (password != confirmPassword)
        {
            ViewBag.Error = "Passwords do not match.";
            return View();
        }

        // Check if username is taken taken
        var existing = await _supabase
            .From<UserInfo>()
            .Where(u => u.Username == username)
            .Get();

        if (existing.Models.Any())
        {
            ViewBag.Error = "Username is already taken.";
            return View();
        }

        var newUser = new UserInfo
        {
            Username = username,
            Password = password
        };

        var response = await _supabase.From<UserInfo>().Insert(newUser);
        var createdUser = response.Models.FirstOrDefault();
    
        if (createdUser is null)
        {
            ViewBag.Error = "Sign up failed. Please try again.";
            return View();
        }

        await SignInCookie(createdUser.Id.ToString(), createdUser.Username);
        return RedirectToAction("Index", "Home");
    }

    // -- Logout ----------------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // -- Delete Account ----------------------------------------------------------------

    [Authorize]
    [HttpGet]
    public IActionResult DeleteAccount() => View();

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> DeleteAccount(string confirm)
    {
        if (confirm != "DELETE")
        {
            ViewBag.Error = "Please type DELETE to confirm.";
            return View();
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await _supabase
            .From<UserInfo>()
            .Where(u => u.Id == userId)
            .Delete();
        
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // -- Helper ----------------------------------------------------------------

    private async Task SignInCookie(string userId, string email)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}