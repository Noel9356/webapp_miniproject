using Microsoft.AspNetCore.Authentication.Cookies;
using webapp_miniproject.Contracts;
using webapp_miniproject.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddScoped<Supabase.Client>(_ =>
    new Supabase.Client(
        builder.Configuration["SupabaseUrl"] ?? throw new InvalidOperationException("SupabaseUrl is not configured."),
        builder.Configuration["SupabaseKey"] ?? throw new InvalidOperationException("SupabaseKey is not configured."),
        new Supabase.SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = true
        }
    )
);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection(); // Only redirect to HTTPS in production
}

app.MapPost("/api/gameGroups", async (
    CreateGameGroupRequest request,
    Supabase.Client client) =>
{
    var gameGroup = new GameGroupInfo
    {
        GameId = request.GameId,
        Title = request.Title,
        Description = request.Description,
        ImageUrl = request.ImageUrl
    };

    var response = await client
        .From<GameGroupInfo>()
        .Insert(gameGroup);

    var newGameGroup = response.Models.First();

    return Results.Ok(newGameGroup.Id);
});

app.MapGet("/api/gameGroups/{id}", async (int id, Supabase.Client client) =>
{
    var response = await client
        .From<GameGroupInfo>()
        .Where(g => g.Id == id)
        .Get();

    var gameGroup = response.Models.FirstOrDefault();

    if (gameGroup is null) return Results.NotFound();

    return Results.Ok(gameGroup);
});

app.MapDelete("/gameGroups/{id}", async (int id, Supabase.Client client) =>
{
    await client
        .From<GameGroupInfo>()
        .Where(g => g.Id == id)
        .Delete();

    return Results.NoContent();
});

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // Must come before UseAuthorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();

// Test Account
// 67010321@kmitl.ac.th
// Asdc5523
