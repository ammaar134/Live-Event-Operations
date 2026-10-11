using System.Security.Claims;
using System.Threading.RateLimiting;
using Fieldroom;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LibraryDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Library")
    ?? "Host=127.0.0.1;Port=55435;Database=fieldroom;Username=fieldroom;Password=SET_LOCAL_ENV", n => n.CommandTimeout(15)));
builder.Services.AddIdentity<Member, IdentityRole>(o =>
{
    o.Password.RequiredLength = 12;
    o.User.RequireUniqueEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LibraryDb>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "fieldroom.session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "fieldroom.csrf"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
builder.Services.AddAuthorization();
builder.Services.AddScoped<AllocationService>();
builder.Services.AddHealthChecks().AddDbContextCheck<LibraryDb>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    foreach (var name in new[] { "auth", "hold" })
        o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = name == "auth" ? 20 : 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var keyPath = builder.Configuration["DataProtectionPath"];
if (!string.IsNullOrWhiteSpace(keyPath)) builder.Services.AddDataProtection().SetApplicationName("Fieldroom").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; font-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (Exception ex)
    {
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        var (status, code, message) = ex switch
        {
            DomainError e => (e.Status, e.Code, e.Message),
            AntiforgeryValidationException => (400, "csrf", "Your security token expired. Refresh and try again."),
            BadHttpRequestException => (400, "invalid_request", "Check the request fields."),
            _ when pg?.SqlState == "23P01" => (409, "occupied", "That copy was just reserved or blocked for maintenance. Your previous booking is unchanged. Choose another interval or copy."),
            _ when pg?.SqlState == "23505" => (409, "duplicate", "This action has already been recorded. Refresh to see the current state."),
            _ when pg?.SqlState is "55P03" or "57014" or "40P01" or "40001" => (503, "retryable", "The desk is busy. Retry the same request shortly."),
            _ when ex is NpgsqlException or TimeoutException || ex.InnerException is NpgsqlException or TimeoutException => (503, "retryable", "The database connection was interrupted. Retry using the same request key."),
            _ => (500, "unexpected", "The request could not be completed. Retry with the same request key.")
        };
        if (status == 500) app.Logger.LogError("Unhandled {ExceptionType}; trace {TraceId}", ex.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (status == 503) context.Response.Headers.RetryAfter = "2";
        await context.Response.WriteAsJsonAsync(new { code, message, traceId = context.TraceIdentifier });
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapHealthChecks("/health/ready");
app.MapGet("/api/session/csrf", (HttpContext c, IAntiforgery a) => new { token = a.GetAndStoreTokens(c).RequestToken });
app.MapGet("/api/session", async (HttpContext c, UserManager<Member> users) =>
{
    var user = await users.GetUserAsync(c.User);
    return user == null ? Results.Json(new { authenticated = false }) : Results.Json(new { authenticated = true, user.Id, user.DisplayName, user.Email, manager = await users.IsInRoleAsync(user, "Manager") });
});
app.MapPost("/api/session/register", async (RegisterRequest r, UserManager<Member> users, SignInManager<Member> signIn) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80 || string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(r.Email, out _) || string.IsNullOrEmpty(r.Password) || r.Password.Length > 128)
        throw new DomainError(400, "validation", "Enter a name, valid email and password of 12–128 characters.");
    var user = new Member { UserName = r.Email.Trim(), Email = r.Email.Trim(), DisplayName = r.Name.Trim() };
    var result = await users.CreateAsync(user, r.Password);
    if (!result.Succeeded) throw new DomainError(400, "registration", "Registration failed. Use an unused email and a 12-character password with upper/lowercase letters, a number and a symbol.");
    await signIn.SignInAsync(user, false);
    return Results.Ok(new { user.DisplayName });
}).RequireRateLimiting("auth");
app.MapPost("/api/session/login", async (LoginRequest r, SignInManager<Member> signIn) =>
{
    if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrEmpty(r.Password) || r.Email.Length > 254 || r.Password.Length > 128) throw new DomainError(400, "validation", "Invalid sign-in details.");
    var result = await signIn.PasswordSignInAsync(r.Email, r.Password, false, true);
    if (!result.Succeeded) throw new DomainError(401, "sign_in", "Sign-in failed. Check your details or try again later.");
    return Results.Ok(new { signedIn = true });
}).RequireRateLimiting("auth");
app.MapPost("/api/session/logout", async (SignInManager<Member> signIn) => { await signIn.SignOutAsync(); return Results.Ok(new { signedOut = true }); }).RequireAuthorization();
app.MapLibraryEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDb>().Database.MigrateAsync();
    await Seed.Run(scope.ServiceProvider, app.Configuration["BootstrapPassword"]);
}
if (!app.Environment.IsDevelopment() && !string.IsNullOrEmpty(app.Configuration["BootstrapPassword"]))
    throw new InvalidOperationException("Development bootstrap is forbidden outside Development.");
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryDb>().Database.MigrateAsync();
    return;
}
app.Run();
public partial class Program;
public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Name, string Email, string Password);
