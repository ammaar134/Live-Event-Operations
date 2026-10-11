using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fieldroom;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests;

[Collection("postgres")]
public class SecurityTests(DatabaseFixture f)
{
    private async Task Token(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/session/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
    }
    private async Task<HttpClient> Login(string email)
    {
        var client = f.App.CreateClient(new() { AllowAutoRedirect = false }); await Token(client);
        var result = await client.PostAsJsonAsync("/api/session/login", new { email, password = f.Password }); Assert.Equal(HttpStatusCode.OK, result.StatusCode); await Token(client); return client;
    }

    [Fact]
    public async Task Actual_cookie_auth_flow_csrf_ownership_roles_and_logout()
    {
        using var anon = f.App.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/desk/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/session/login", new { email = "alex@example.test", password = f.Password })).StatusCode);
        using var member = await Login("alex@example.test"); using var other = await Login("sam@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/desk/inventory")).StatusCode);
        await using var db = f.Db(); await db.Database.ExecuteSqlRawAsync("UPDATE \"Allocations\" SET \"State\"='Released' WHERE \"State\"='Hold'");
        var asset = await f.Asset(); member.DefaultRequestHeaders.Add("Idempotency-Key", DatabaseFixture.Key());
        var hold = await member.PostAsJsonAsync("/api/holds", DatabaseFixture.Interval(asset)); Assert.Equal(HttpStatusCode.OK, hold.StatusCode);
        var h = await hold.Content.ReadFromJsonAsync<AllocationResult>();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/bookings/{h!.Id}")).StatusCode);
        other.DefaultRequestHeaders.Add("Idempotency-Key", DatabaseFixture.Key());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/bookings/{h.Id}/confirm", new { reason = "stolen" })).StatusCode);
        member.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PostAsJsonAsync($"/api/bookings/{h.Id}/confirm", new { reason = "without csrf" })).StatusCode);
        await Token(member);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync($"/api/bookings/{h.Id}/confirm", new { reason = "accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/bookings/{h.Id}/pickup", new { reason = "self pickup" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/session/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/bookings")).StatusCode);
    }

    [Fact]
    public async Task Registration_ignores_client_role_and_session_restores()
    {
        using var client = f.App.CreateClient(new() { AllowAutoRedirect = false }); await Token(client);
        var email = $"new-{Guid.NewGuid():N}@example.test";
        var r = await client.PostAsJsonAsync("/api/session/register", new { name = "Taylor", email, password = f.Password, role = "Manager", manager = true }); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session"); Assert.True(session.GetProperty("authenticated").GetBoolean()); Assert.False(session.GetProperty("manager").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/desk/audit")).StatusCode);
    }

    [Fact]
    public async Task Expired_authentication_ticket_is_rejected()
    {
        using var scope = f.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<Member>>().CreateAsync((await users.FindByIdAsync(f.Member1))!);
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, new() { IssuedUtc = DateTimeOffset.UtcNow.AddDays(-2), ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1) }, IdentityConstants.ApplicationScheme);
        var expired = options.TicketDataFormat.Protect(ticket);
        using var client = f.App.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }); client.DefaultRequestHeaders.Add("Cookie", $"fieldroom.session={expired}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bookings")).StatusCode);
    }

    [Fact]
    public async Task Manager_policy_retry_recovers_original_result_without_duplicate_audit()
    {
        using var client = await Login("desk@fieldroom.example");
        var policy = (await client.GetFromJsonAsync<JsonElement>("/api/policy")).GetProperty("policy").Deserialize<LendingPolicy>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var initialVersion = policy.Version; policy.BufferMinutes = 60;
        client.DefaultRequestHeaders.Add("Idempotency-Key", DatabaseFixture.Key());
        var request = new { policy, reason = "Review local lending policy" };
        var first = await client.PostAsJsonAsync("/api/desk/policy", request); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var retry = await client.PostAsJsonAsync("/api/desk/policy", request); Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        await using var db = f.Db(); Assert.Equal(initialVersion + 1, (await db.Policies.SingleAsync()).Version);
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "PolicyChanged" && x.Reason.Contains("Review local lending policy")));
    }

    [Fact]
    public async Task Product_retry_and_optimistic_version_prevent_lost_updates()
    {
        using var client = await Login("desk@fieldroom.example"); await using var db = f.Db(); var product = await db.Products.AsNoTracking().FirstAsync();
        var r = new ProductRequest(product.Name, product.Category, product.Description, product.Specifications, product.Accessories, "Description checked", product.Version);
        client.DefaultRequestHeaders.Add("Idempotency-Key", DatabaseFixture.Key());
        var first = await client.PostAsJsonAsync($"/api/desk/products/{product.Id}", r); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var retry = await client.PostAsJsonAsync($"/api/desk/products/{product.Id}", r); Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", DatabaseFixture.Key());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/desk/products/{product.Id}", r)).StatusCode);
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.Action == "ProductEdited" && x.Reason.Contains("Description checked")));
    }
}
