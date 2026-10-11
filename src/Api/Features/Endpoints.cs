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
                .Select(x => new
                {
                    x.Id,
                    x.Reference,
                    x.Start,
                    x.End,
                    state = x.State == "Hold" && x.ExpiresAt <= now ? "Expired" : x.State,
                    x.ExpiresAt,
                    asset = x.Asset.Tag,
                    product = x.Asset.Product.Name,
                    image = x.Asset.Product.Image,
                    atRisk = x.State == "Confirmed" && (x.Asset.State != "Ready" || x.Asset.ReadyAt > (x.Start > now ? x.Start : now) || db.Checkouts.Any(k => k.AssetId == x.AssetId && k.ReturnedAt == null && k.Allocation.End <= now))
                }).ToListAsync();
        }).RequireAuthorization();
        app.MapGet("/api/bookings/{id:guid}", async (Guid id, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            var a = await db.Allocations.AsNoTracking().Include(x => x.Asset).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.Id == id && (c.User.IsInRole("Manager") || x.MemberId == Actor(c))) ?? throw new DomainError(404, "not_found", "Booking not found.");
            var now = await service.Clock();
            var history = await db.AuditEvents.AsNoTracking().Where(x => x.AllocationId == id).OrderBy(x => x.Id).Select(x => new { x.Action, x.Reason, x.At }).ToListAsync();
            var custody = await db.Checkouts.AsNoTracking().Where(x => x.AllocationId == id).Select(x => new { x.PickedUpAt, x.ReturnedAt, x.Condition, x.AccessoryCheck }).SingleOrDefaultAsync();
            return new
            {
                a.Id,
                a.AssetId,
                a.Reference,
                a.Start,
                a.End,
                a.ExpiresAt,
                state = a.State == "Hold" && a.ExpiresAt <= now ? "Expired" : a.State,
                asset = a.Asset.Tag,
                product = a.Asset.Product.Name,
                image = a.Asset.Product.Image,
                a.Asset.Location,
                a.Asset.Product.Accessories,
                policy = JsonSerializer.Deserialize<LendingPolicy>(a.PolicyJson, AllocationService.Json),
                serverTime = now,
                history,
                custody,
                atRisk = a.State == "Confirmed" && (a.Asset.State != "Ready" || a.Asset.ReadyAt > (a.Start > now ? a.Start : now) || await db.Checkouts.AnyAsync(x => x.AssetId == a.AssetId && x.ReturnedAt == null && x.Allocation.End <= now))
            };
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
            return new
            {
                serverTime = now,
                total = await query.CountAsync(),
                bookings = await query.OrderBy(x => x.Start).Skip((Math.Clamp(page ?? 1, 1, 1000) - 1) * 50).Take(50)
                .Select(x => new
                {
                    x.Id,
                    x.Reference,
                    x.State,
                    x.Start,
                    x.End,
                    member = x.Member.DisplayName,
                    asset = x.Asset.Tag,
                    product = x.Asset.Product.Name,
                    overdue = x.State == "CheckedOut" && x.End <= now,
                    atRisk = x.State == "Confirmed" && (x.Asset.State != "Ready" || x.Asset.ReadyAt > (x.Start > now ? x.Start : now) || db.Checkouts.Any(k => k.AssetId == x.AssetId && k.ReturnedAt == null && k.Allocation.End <= now))
                }).ToListAsync()
            };
        });
        desk.MapGet("/inventory", async (LibraryDb db) => await db.Products.AsNoTracking().OrderBy(x => x.Name).Select(x => new
        {
            x.Id,
            x.Version,
            x.Name,
            x.Category,
            x.Description,
            x.Specifications,
            x.Accessories,
            x.Image,
            assets = x.Assets.Select(a => new { a.Id, a.Tag, a.Condition, a.State, a.Location, a.ReadyAt })
        }).ToListAsync());
        desk.MapGet("/audit", async (LibraryDb db, int? page) => await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Id).Skip((Math.Clamp(page ?? 1, 1, 1000) - 1) * 100).Take(100).ToListAsync());
        desk.MapPost("/maintenance", async (MaintenanceRequest r, HttpContext c, AllocationService service) => Json(await service.Maintenance(Actor(c), Key(c), new IntervalRequest(r.AssetId, r.Start, r.End), r.Reason)));
        desk.MapPost("/assets/{id:guid}/{action}", async (Guid id, string action, ChangeRequest r, HttpContext c, AllocationService service) => Json(await service.AssetChange(Actor(c), id, action, Key(c), r)));
        desk.MapPost("/products/{id:guid}", async (Guid id, ProductRequest r, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            if (r.Name.Length is < 2 or > 120 || r.Category is not ("Cameras" or "Tools" or "Outdoors") || r.Description.Length is < 10 or > 2000 || r.Specifications.Length > 1000 || r.Accessories.Length > 1000 || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500)
                throw new DomainError(400, "validation", "Check product field lengths, category and reason.");
            return Json(await service.Metadata(Actor(c), "product-edit", Key(c), new { id, r }, false, async (_, now) =>
            {
                var p = (await db.Products.FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\"={id} FOR UPDATE").ToListAsync()).SingleOrDefault() ?? throw new DomainError(404, "not_found", "Product not found.");
                if (p.Version != r.Version) throw new DomainError(409, "stale_product", "Another manager updated this product. Refresh before editing.");
                p.Name = r.Name; p.Category = r.Category; p.Description = r.Description; p.Specifications = r.Specifications; p.Accessories = r.Accessories;
                p.Version++;
                service.Audit(Actor(c), "ProductEdited", $"Product {id}: {r.Reason}", now);
                return new { p.Id, p.Version };
            }));
        });
        desk.MapPost("/policy", async (PolicyRequest r, HttpContext c, LibraryDb db, AllocationService service) =>
        {
            PolicyRules.Validate(r.Policy);
            if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500) throw new DomainError(400, "reason_required", "Provide a reason for this policy change.");
            return Json(await service.Metadata(Actor(c), "policy-edit", Key(c), r, true, (p, now) =>
            {
                if (r.Policy.Version != p.Version) throw new DomainError(409, "stale_policy", "Policy changed since you opened it. Refresh first.");
                r.Policy.Id = 1; r.Policy.Version = p.Version + 1; db.Policies.Attach(p); db.Entry(p).CurrentValues.SetValues(r.Policy);
                service.Audit(Actor(c), "PolicyChanged", $"Version {r.Policy.Version}: {r.Reason}", now);
                return Task.FromResult<object>(r.Policy);
            }));
        });
        desk.MapPost("/products", async (NewProductRequest r, HttpContext c, LibraryDb db, AllocationService service) => Json(await service.Metadata(Actor(c), "product-create", Key(c), r, false, (_, now) =>
        {
            if (r.Name.Length is < 2 or > 120 || r.Category is not ("Cameras" or "Tools" or "Outdoors") || r.Description.Length is < 10 or > 2000 || r.Specifications.Length > 1000 || r.Accessories.Length > 1000 || !new[] { "camera", "compact", "drill", "sander", "tent", "pack", "lantern", "mat", "cookset", "tripod", "microphone", "light", "clamps", "measure", "jigsaw" }.Contains(r.Image) || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500)
                throw new DomainError(400, "validation", "Check product fields, illustration and reason.");
            var p = new Product { Name = r.Name, Category = r.Category, Description = r.Description, Specifications = r.Specifications, Accessories = r.Accessories, Image = r.Image };
            db.Products.Add(p); service.Audit(Actor(c), "ProductCreated", $"Product {p.Id}: {r.Reason}", now); return Task.FromResult<object>(new { p.Id });
        })));
        desk.MapPost("/assets", async (NewAssetRequest r, HttpContext c, LibraryDb db, AllocationService service) => Json(await service.Metadata(Actor(c), "asset-create", Key(c), r, false, async (_, now) =>
        {
            if (r.Tag.Length is < 3 or > 40 || !r.Tag.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') || r.Condition.Length is < 3 or > 150 || r.Location.Length is < 3 or > 200 || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 500 || !await db.Products.AnyAsync(x => x.Id == r.ProductId))
                throw new DomainError(400, "validation", "Choose an existing product, unique inventory label, condition, location and reason.");
            var asset = new Asset { ProductId = r.ProductId, Tag = r.Tag, Condition = r.Condition, Location = r.Location };
            db.Assets.Add(asset); await db.SaveChangesAsync(); service.Audit(Actor(c), "AssetAdded", r.Reason, now, asset.Id); return new { asset.Id, asset.Tag };
        })));
    }
}
public sealed record MaintenanceRequest(Guid AssetId, string Start, string End, string Reason);
public sealed record ProductRequest(string Name, string Category, string Description, string Specifications, string Accessories, string Reason, int Version);
public sealed record NewProductRequest(string Name, string Category, string Description, string Specifications, string Accessories, string Reason, string Image);
public sealed record NewAssetRequest(Guid ProductId, string Tag, string Condition, string Location, string Reason);
public sealed record PolicyRequest(LendingPolicy Policy, string Reason);
