using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Fieldroom;

public static class Endpoints
{
    private static string Actor(HttpContext c) => c.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Key(HttpContext c) => c.Request.Headers["Idempotency-Key"].ToString();
    private static IResult Json(string value) => Results.Content(value, "application/json");

    public static void MapLibraryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/policy", async (LibraryDb db, AllocationService service) => new { policy = await db.Policies.AsNoTracking().SingleAsync(), serverTime = await service.Clock() });
        app.MapGet("/api/catalog", async (LibraryDb db, AllocationService service, string? q, string? category, string? start, string? end, string? sort, int? page) =>
        {
            var policy = await db.Policies.AsNoTracking().SingleAsync(); var now = await service.Clock();
            DateTime? s = null, e = null;
            if (start != null || end != null) (s, e) = PolicyRules.Interval(start ?? "", end ?? "", policy, now);
            var query = db.Products.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var term = q[..Math.Min(q.Length, 100)].ToLower(); query = query.Where(x => x.Name.ToLower().Contains(term) || x.Description.ToLower().Contains(term)); }
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
            var total = await query.CountAsync();
            query = sort == "category" ? query.OrderBy(x => x.Category).ThenBy(x => x.Name) : query.OrderBy(x => x.Name);
            var products = await query.Include(x => x.Assets).Skip((Math.Clamp(page ?? 1, 1, 100) - 1) * 24).Take(24).ToListAsync();
            var ids = products.SelectMany(x => x.Assets).Select(x => x.Id).ToArray();
            var allocations = await db.Allocations.AsNoTracking().Where(x => ids.Contains(x.AssetId) && States.Blocking.Contains(x.State) && (x.State != "Hold" || x.ExpiresAt > now) && x.End > now.AddDays(-1)).ToListAsync();
            var overdue = await db.Checkouts.Where(x => ids.Contains(x.AssetId) && x.ReturnedAt == null && x.Allocation.End <= now).Select(x => x.AssetId).ToListAsync();
            return new { total, serverTime = now, policy, products = products.Select(p => new { p.Id, p.Name, p.Category, p.Description, p.Image, assets = p.Assets.Select(a => new { a.Id, a.Tag, a.State, available = a.State == "Ready" && !(a.ReadyAt > now) && !overdue.Contains(a.Id) && (s == null || !allocations.Any(x => x.AssetId == a.Id && x.Occupied.LowerBound < e!.Value.AddMinutes(policy.BufferMinutes) && x.Occupied.UpperBound > s)), atRisk = overdue.Contains(a.Id) }) }) };
        });
        app.MapGet("/api/products/{id:guid}", async (Guid id, LibraryDb db, AllocationService service) =>
        {
            var p = await db.Products.AsNoTracking().Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "not_found", "Equipment not found.");
            var now = await service.Clock();
            var schedule = await db.Allocations.AsNoTracking().Where(x => x.Asset.ProductId == id && States.Blocking.Contains(x.State) && (x.State != "Hold" || x.ExpiresAt > now) && x.End > now.AddDays(-1) && x.Start < now.AddDays(90))
                .OrderBy(x => x.Start).Take(500).Select(x => new { x.AssetId, x.Start, x.End, blockedUntil = x.Occupied.UpperBound, kind = x.State == "Maintenance" ? "Maintenance" : "Reserved" }).ToListAsync();
            var overdue = await db.Checkouts.Where(x => x.Asset.ProductId == id && x.ReturnedAt == null && x.Allocation.End <= now).Select(x => x.AssetId).ToListAsync();
            return new { p.Id, p.Name, p.Category, p.Description, p.Specifications, p.Accessories, p.Image, assets = p.Assets.Select(a => new { a.Id, a.Tag, a.Condition, a.Location, a.State, a.ReadyAt, atRisk = overdue.Contains(a.Id) }), schedule, serverTime = now, policy = await db.Policies.AsNoTracking().SingleAsync() };
        });
        app.MapPost("/api/holds", async (IntervalRequest r, HttpContext c, AllocationService service) => Json(await service.Hold(Actor(c), Key(c), r))).RequireAuthorization().RequireRateLimiting("hold");
        app.MapGet("/api/bookings", async (HttpContext c, LibraryDb db, AllocationService service) =>
        {
            var now = await service.Clock();
            return await db.Allocations.AsNoTracking().Where(x => x.MemberId == Actor(c) && x.State != "Maintenance" && x.State != "MaintenanceEnded").OrderByDescending(x => x.CreatedAt).Take(200)
                .Select(x => new { x.Id, x.Reference, x.Start, x.End, state = x.State == "Hold" && x.ExpiresAt <= now ? "Expired" : x.State, x.ExpiresAt, asset = x.Asset.Tag, product = x.Asset.Product.Name, image = x.Asset.Product.Image,
                    atRisk = db.Checkouts.Any(k => k.AssetId == x.AssetId && k.ReturnedAt == null && k.Allocation.End <= now) && x.State == "Confirmed" }).ToListAsync();
        }).RequireAuthorization();
        app.MapGet("/api/bookings/{id:guid}", async (Guid id, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            var a = await db.Allocations.AsNoTracking().Include(x => x.Asset).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.Id == id && (c.User.IsInRole("Manager") || x.MemberId == Actor(c))) ?? throw new DomainError(404, "not_found", "Booking not found.");
            var now = await service.Clock();
            var history = await db.AuditEvents.AsNoTracking().Where(x => x.AllocationId == id).OrderBy(x => x.Id).Select(x => new { x.Action, x.Reason, x.At }).ToListAsync();
            var custody = await db.Checkouts.AsNoTracking().Where(x => x.AllocationId == id).Select(x => new { x.PickedUpAt, x.ReturnedAt, x.Condition, x.AccessoryCheck }).SingleOrDefaultAsync();
            return new { a.Id, a.AssetId, a.Reference, a.Start, a.End, a.ExpiresAt, state = a.State == "Hold" && a.ExpiresAt <= now ? "Expired" : a.State,
                asset = a.Asset.Tag, product = a.Asset.Product.Name, image = a.Asset.Product.Image, a.Asset.Location, a.Asset.Product.Accessories,
                policy = JsonSerializer.Deserialize<LendingPolicy>(a.PolicyJson, AllocationService.Json), serverTime = now, history, custody,
                atRisk = a.State == "Confirmed" && (a.Asset.State != "Ready" || await db.Checkouts.AnyAsync(x => x.AssetId == a.AssetId && x.ReturnedAt == null && x.Allocation.End <= now)) };
        }).RequireAuthorization();
        app.MapPost("/api/bookings/{id:guid}/{action}", async (Guid id, string action, ChangeRequest r, HttpContext c, AllocationService service) => Json(await service.Change(Actor(c), c.User.IsInRole("Manager"), id, action, Key(c), r))).RequireAuthorization();

        var desk = app.MapGroup("/api/desk").RequireAuthorization(p => p.RequireRole("Manager"));
        desk.MapGet("/bookings", async (LibraryDb db, AllocationService service, string? q, string? state, int? page) =>
        {
            var now = await service.Clock();
            var query = db.Allocations.AsNoTracking().Where(x => x.Reference != null || x.State == "Maintenance");
            if (!string.IsNullOrWhiteSpace(q)) { var term = q[..Math.Min(q.Length, 100)].ToLower(); query = query.Where(x => (x.Reference ?? "").ToLower().Contains(term) || x.Asset.Tag.ToLower().Contains(term) || x.Member.DisplayName.ToLower().Contains(term)); }
            if (state == "Overdue") query = query.Where(x => x.State == "CheckedOut" && x.End <= now);
            else if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.State == state);
            return new { serverTime = now, total = await query.CountAsync(), bookings = await query.OrderBy(x => x.Start).Skip((Math.Clamp(page ?? 1, 1, 1000) - 1) * 50).Take(50)
                .Select(x => new { x.Id, x.Reference, x.State, x.Start, x.End, member = x.Member.DisplayName, asset = x.Asset.Tag, product = x.Asset.Product.Name, overdue = x.State == "CheckedOut" && x.End <= now,
                    atRisk = x.State == "Confirmed" && (x.Asset.State != "Ready" || db.Checkouts.Any(k => k.AssetId == x.AssetId && k.ReturnedAt == null && k.Allocation.End <= now)) }).ToListAsync() };
        });
        desk.MapGet("/inventory", async (LibraryDb db) => await db.Products.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Category, x.Description, x.Specifications, x.Accessories, x.Image,
            assets = x.Assets.Select(a => new { a.Id, a.Tag, a.Condition, a.State, a.Location, a.ReadyAt }) }).ToListAsync());
        desk.MapGet("/audit", async (LibraryDb db, int? page) => await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Skip((Math.Clamp(page ?? 1, 1, 1000) - 1) * 100).Take(100).ToListAsync());
        desk.MapPost("/maintenance", async (MaintenanceRequest r, HttpContext c, AllocationService service) => Json(await service.Maintenance(Actor(c), Key(c), new IntervalRequest(r.AssetId, r.Start, r.End), r.Reason)));
        desk.MapPost("/assets/{id:guid}/{action}", async (Guid id, string action, ChangeRequest r, HttpContext c, AllocationService service) => Json(await service.AssetChange(Actor(c), id, action, Key(c), r)));
        desk.MapPost("/products/{id:guid}", async (Guid id, ProductRequest r, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            if (r.Name.Length is < 2 or > 120 || r.Category is not ("Cameras" or "Tools" or "Outdoors") || r.Description.Length is < 10 or > 2000 || r.Specifications.Length > 1000 || r.Accessories.Length > 1000 || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500)
                throw new DomainError(400, "validation", "Check product field lengths, category and reason.");
            await using var tx = await db.Database.BeginTransactionAsync();
            var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id) ?? throw new DomainError(404, "not_found", "Product not found.");
            p.Name = r.Name; p.Category = r.Category; p.Description = r.Description; p.Specifications = r.Specifications; p.Accessories = r.Accessories;
            service.Audit(Actor(c), "ProductEdited", $"Product {id}: {r.Reason}", await service.Clock());
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { p.Id });
        });
        desk.MapPost("/policy", async (PolicyRequest r, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            PolicyRules.Validate(r.Policy);
            if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new DomainError(400, "reason_required", "Provide a reason for this policy change.");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
            var p = (await db.Policies.FromSqlRaw("SELECT * FROM \"Policies\" WHERE \"Id\" = 1 FOR UPDATE").ToListAsync()).Single();
            if (r.Policy.Version != p.Version) throw new DomainError(409, "stale_policy", "Policy changed since you opened it. Refresh first.");
            r.Policy.Id = 1; r.Policy.Version = p.Version + 1; db.Entry(p).CurrentValues.SetValues(r.Policy);
            service.Audit(Actor(c), "PolicyChanged", $"Version {r.Policy.Version}: {r.Reason}", await service.Clock());
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(r.Policy);
        });
    }
}
public sealed record MaintenanceRequest(Guid AssetId, string Start, string End, string Reason);
public sealed record ProductRequest(string Name, string Category, string Description, string Specifications, string Accessories, string Reason);
public sealed record PolicyRequest(LendingPolicy Policy, string Reason);
