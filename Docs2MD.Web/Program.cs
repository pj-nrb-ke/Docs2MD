using Docs2MD.Web.Components;
using Docs2MD.Web.Data;
using Docs2MD.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database & Identity ──────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// ── External OAuth ───────────────────────────────────────────────────────────
var googleId     = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var msId         = builder.Configuration["Authentication:Microsoft:ClientId"];
var msSecret     = builder.Configuration["Authentication:Microsoft:ClientSecret"];

var authBuilder = builder.Services.AddAuthentication();

if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
    authBuilder.AddGoogle(o =>
    {
        o.ClientId     = googleId;
        o.ClientSecret = googleSecret;
    });

if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
    authBuilder.AddMicrosoftAccount(o =>
    {
        o.ClientId     = msId;
        o.ClientSecret = msSecret;
    });

// ── Razor Pages (auth routes) + Blazor ───────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ConversionService>();
builder.Services.AddScoped<ConversionState>();

// ── Cascade auth state to Blazor components ──────────────────────────────────
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// ── Migrate DB on startup ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// OAuth callback endpoints (handled by ASP.NET Core Identity)
app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
