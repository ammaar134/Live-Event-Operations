using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Fieldroom;

public sealed record IntervalRequest(Guid AssetId, string Start, string End);
public sealed record ChangeRequest(string Reason, string? Start = null, string? End = null, string? Condition = null, string? Accessories = null);
public sealed record AllocationResult(Guid Id, Guid AssetId, string State, string? Reference, DateTime Start, DateTime End, DateTime? ExpiresAt, DateTime ServerTime, LendingPolicy Policy);

public sealed class AllocationService(LibraryDb db)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DateTime> Clock() => await db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();

    public void Audit(string actor, string action, string reason, DateTime at, Guid? asset = null, Guid? allocation = null) =>
        db.AuditEvents.Add(new AuditEvent { ActorId = actor, Action = action, Reason = reason, At = at, AssetId = asset, AllocationId = allocation });

    // Shared lock protocol for every mutation affecting an asset's occupancy or custody.
    public async Task<string> Guard(string actor, Guid assetId, string operation, string key, object payload,
        Func<Asset, LendingPolicy, DateTime, Task<object>> change)
    {
        if (key.Length is < 16 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new DomainError(400, "idempotency_required", "A valid Idempotency-Key is required. Retry with the same key.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json))));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'");
        var policy = (await db.Policies.FromSqlRaw("SELECT * FROM \"Policies\" WHERE \"Id\" = 1 FOR SHARE").AsNoTracking().ToListAsync()).Single();
        _ = (await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {actor} FOR UPDATE").ToListAsync()).SingleOrDefault()
            ?? throw new DomainError(401, "session_expired", "Please sign in again.");
        var asset = (await db.Assets.FromSqlInterpolated($"SELECT * FROM \"Assets\" WHERE \"Id\" = {assetId} FOR UPDATE").ToListAsync()).SingleOrDefault()
            ?? throw new DomainError(404, "not_found", "Equipment not found.");
        var previous = await db.Idempotency.FindAsync(actor, operation, key);
        if (previous != null)
        {
            if (previous.Digest != digest) throw new DomainError(409, "key_reused", "This retry key belongs to a different request.");
            await tx.CommitAsync();
            return previous.Response;
        }
        var now = await Clock();
        var expired = await db.Allocations.Where(x => x.AssetId == assetId && x.State == "Hold" && x.ExpiresAt <= now).ToListAsync();
        foreach (var hold in expired)
        {
            hold.State = "Expired";
            Audit(actor, "HoldExpired", "Server-controlled hold elapsed", now, assetId, hold.Id);
        }
        await db.SaveChangesAsync();
        var result = await change(asset, policy, now);
        var response = JsonSerializer.Serialize(result, Json);
        db.Idempotency.Add(new Idempotency { MemberId = actor, Operation = operation, Key = key, Digest = digest, Response = response, CreatedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return response;
    }

    private async Task Ready(Asset asset, DateTime now, bool pickup = false)
    {
        if (asset.State != "Ready" || asset.ReadyAt > now)
            throw new DomainError(409, "asset_unavailable", "This copy needs inspection, turnaround, or has been retired. Choose another copy.");
        var open = await db.Checkouts.Include(x => x.Allocation).SingleOrDefaultAsync(x => x.AssetId == asset.Id && x.ReturnedAt == null);
        if (open != null && (pickup || open.Allocation.End <= now))
            throw new DomainError(409, "physical_custody", "This copy has not been returned. The lending desk must receive it before this action.");
    }

    private static AllocationResult View(Allocation a, DateTime now) => new(a.Id, a.AssetId, a.State, a.Reference,
        a.Start, a.End, a.ExpiresAt, now, JsonSerializer.Deserialize<LendingPolicy>(a.PolicyJson, Json)!);

    public Task<string> Hold(string actor, string key, IntervalRequest request) => Guard(actor, request.AssetId, "hold", key, request,
        async (asset, policy, now) =>
        {
            await Ready(asset, now);
            if (await db.Allocations.CountAsync(x => x.MemberId == actor && x.State == "Hold" && x.ExpiresAt > now) >= policy.HoldLimit)
                throw new DomainError(409, "hold_limit", "You have reached your active hold limit. Release a hold or let it expire.");
            var (start, end) = PolicyRules.Interval(request.Start, request.End, policy, now);
            var a = new Allocation { AssetId = asset.Id, MemberId = actor, Start = start, End = end,
                Occupied = new NpgsqlRange<DateTime>(start, true, end.AddMinutes(policy.BufferMinutes), false),
                ExpiresAt = now.AddSeconds(policy.HoldSeconds), CreatedAt = now, PolicyJson = JsonSerializer.Serialize(policy, Json) };
            db.Allocations.Add(a);
            Audit(actor, "HoldCreated", "Member selected this specific copy", now, asset.Id, a.Id);
            return View(a, now);
        });

    public async Task<string> Change(string actor, bool manager, Guid id, string action, string key, ChangeRequest request)
    {
        var target = await db.Allocations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (manager || x.MemberId == actor))
            ?? throw new DomainError(404, "not_found", "Booking not found.");
        return await Guard(actor, target.AssetId, action, key, new { id, request }, async (asset, policy, now) =>
        {
            var a = (await db.Allocations.FromSqlInterpolated($"SELECT * FROM \"Allocations\" WHERE \"Id\" = {id} FOR UPDATE").ToListAsync()).Single();
            var snapshot = JsonSerializer.Deserialize<LendingPolicy>(a.PolicyJson, Json)!;
            if (!manager && a.MemberId != actor) throw new DomainError(404, "not_found", "Booking not found.");
            if (action is "pickup" or "return" or "no-show" or "end-maintenance")
                if (!manager) throw new DomainError(403, "forbidden", "The lending desk manages this action.");
            if (action != "confirm" && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500))
                throw new DomainError(400, "reason_required", "Provide a short reason (up to 500 characters).");
            switch (action)
            {
                case "confirm":
                    if (a.MemberId != actor) throw new DomainError(403, "forbidden", "Only the hold owner can confirm it.");
                    if (a.State != "Hold" || a.ExpiresAt <= now) throw new DomainError(409, "hold_expired", "This hold has expired or was released. Choose your dates again.");
                    await Ready(asset, now);
                    a.State = "Confirmed";
                    a.Reference = "FR-" + a.Id.ToString("N")[..12].ToUpperInvariant();
                    // The policy offered with the hold is the policy accepted on confirmation.
                    break;
                case "release":
                    if (a.State != "Hold") throw Conflict();
                    a.State = "Released";
                    break;
                case "cancel":
                    if (!States.CanCancel(a.State)) throw Conflict();
                    if (!manager && now > a.Start.AddMinutes(-snapshot.CancellationMinutes))
                        throw new DomainError(409, "cutoff", "The cancellation cutoff has passed. Contact the lending desk.");
                    a.State = "Cancelled";
                    break;
                case "reschedule":
                    if (a.State != "Confirmed") throw Conflict();
                    if (now > a.Start.AddMinutes(-snapshot.CancellationMinutes)) throw new DomainError(409, "cutoff", "The change cutoff has passed.");
                    await Ready(asset, now);
                    var (s, e) = PolicyRules.Interval(request.Start ?? "", request.End ?? "", snapshot, now);
                    a.Start = s; a.End = e;
                    a.Occupied = new NpgsqlRange<DateTime>(s, true, e.AddMinutes(snapshot.BufferMinutes), false);
                    break;
                case "pickup":
                    if (a.State != "Confirmed") throw Conflict();
                    if (now < a.Start || now >= a.End) throw new DomainError(409, "pickup_window", "Pickup is allowed during the reserved interval only.");
                    await Ready(asset, now, true);
                    db.Checkouts.Add(new Checkout { AllocationId = id, AssetId = asset.Id, PickedUpAt = now });
                    a.State = "CheckedOut";
                    break;
                case "return":
                    if (a.State != "CheckedOut") throw Conflict();
                    if (request.Condition is not ("Good" or "Damaged") || string.IsNullOrWhiteSpace(request.Accessories) || request.Accessories.Length > 500)
                        throw new DomainError(400, "return_check", "Record Good or Damaged condition and an accessory check.");
                    var checkout = await db.Checkouts.SingleAsync(x => x.AllocationId == id && x.ReturnedAt == null);
                    checkout.ReturnedAt = now; checkout.Condition = request.Condition; checkout.AccessoryCheck = request.Accessories;
                    a.State = "Returned";
                    asset.Condition = request.Condition;
                    asset.ReadyAt = now.AddMinutes(snapshot.BufferMinutes);
                    if (request.Condition == "Damaged") asset.State = "Inspection";
                    // Original bounded occupancy is retained, including after an early return.
                    break;
                case "no-show":
                    if (a.State != "Confirmed" || now < a.End) throw Conflict();
                    a.State = "NoShow";
                    break;
                case "end-maintenance":
                    if (a.State != "Maintenance") throw Conflict();
                    a.State = "MaintenanceEnded";
                    break;
                default: throw new DomainError(404, "not_found", "Unknown action.");
            }
            Audit(actor, action, action == "confirm" ? "Member accepted the held interval and policy" : request.Reason.Trim(), now, asset.Id, a.Id);
            return View(a, now);
        });
    }

    public Task<string> Maintenance(string actor, string key, IntervalRequest request, string reason) => Guard(actor, request.AssetId, "maintenance", key, new { request, reason },
        async (asset, policy, now) =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new DomainError(400, "reason_required", "Provide a reason for maintenance.");
            if (asset.State == "Retired" || await db.Checkouts.AnyAsync(x => x.AssetId == asset.Id && x.ReturnedAt == null))
                throw new DomainError(409, "physical_custody", "Receive this copy before scheduling maintenance; retired copies cannot be allocated.");
            var s = PolicyRules.Resolve(request.Start, policy.TimeZone); var e = PolicyRules.Resolve(request.End, policy.TimeZone);
            if (s >= e || e <= now || e > now.AddDays(365)) throw new DomainError(400, "duration", "Choose a valid maintenance interval within one year.");
            var a = new Allocation { AssetId = asset.Id, MemberId = actor, State = "Maintenance", Start = s, End = e,
                Occupied = new NpgsqlRange<DateTime>(s, true, e, false), CreatedAt = now, PolicyJson = JsonSerializer.Serialize(policy, Json) };
            db.Allocations.Add(a); Audit(actor, "MaintenanceCreated", reason, now, asset.Id, a.Id);
            return View(a, now);
        });

    public Task<string> AssetChange(string actor, Guid id, string action, string key, ChangeRequest request) => Guard(actor, id, "asset-" + action, key, new { id, request },
        async (asset, policy, now) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500) throw new DomainError(400, "reason_required", "Provide a reason.");
            if (action == "retire")
            {
                if (asset.State == "Retired") throw Conflict();
                if (await db.Checkouts.AnyAsync(x => x.AssetId == id && x.ReturnedAt == null) ||
                    await db.Allocations.AnyAsync(x => x.AssetId == id && States.Blocking.Contains(x.State) && x.End > now))
                    throw new DomainError(409, "commitments", "Resolve future holds, bookings and maintenance, and receive open loans before retiring this copy.");
                asset.State = "Retired";
            }
            else if (action == "inspect")
            {
                if (asset.State != "Inspection" || await db.Checkouts.AnyAsync(x => x.AssetId == id && x.ReturnedAt == null)) throw Conflict();
                asset.State = "Ready"; asset.Condition = "Inspected — good"; asset.ReadyAt = now.AddMinutes(policy.BufferMinutes);
            }
            else if (action == "edit")
            {
                if (string.IsNullOrWhiteSpace(request.Condition) || request.Condition.Length > 150) throw new DomainError(400, "condition", "Enter a condition description up to 150 characters.");
                asset.Condition = request.Condition;
            }
            else throw new DomainError(404, "not_found", "Unknown asset action.");
            Audit(actor, "Asset" + action, request.Reason, now, id);
            return new { asset.Id, asset.State, asset.Condition, asset.ReadyAt };
        });

    private static DomainError Conflict() => new(409, "state_conflict", "This action is no longer available. Refresh to see the current state.");
}
