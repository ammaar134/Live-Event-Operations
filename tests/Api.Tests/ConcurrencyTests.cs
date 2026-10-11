using System.Text.Json;
using Fieldroom;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Api.Tests;

[Collection("postgres")]
public class ConcurrencyTests(DatabaseFixture f)
{
    private async Task<AllocationResult> Hold(string member, IntervalRequest r, string? key = null)
    {
        await using var db = f.Db(); var json = await new AllocationService(db).Hold(member, key ?? DatabaseFixture.Key(), r);
        return JsonSerializer.Deserialize<AllocationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private async Task<string> Change(string member, Guid id, string action, ChangeRequest? r = null, bool manager = false, string? key = null)
    {
        await using var db = f.Db(); return await new AllocationService(db).Change(member, manager, id, action, key ?? DatabaseFixture.Key(), r ?? new("Test transition"));
    }
    private async Task ClearHolds()
    {
        await using var db = f.Db(); await db.Database.ExecuteSqlRawAsync("UPDATE \"Allocations\" SET \"State\"='Released' WHERE \"State\"='Hold'");
    }

    [Fact]
    public async Task Simultaneous_overlapping_holds_have_one_winner_and_one_audit()
    {
        await ClearHolds(); var asset = await f.Asset(); var request = DatabaseFixture.Interval(asset);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Race(string member) { await using var db = f.Db(); await db.Database.OpenConnectionAsync(); await gate.Task; try { await new AllocationService(db).Hold(member, DatabaseFixture.Key(), request); return true; } catch (DomainError e) when (e.Code == "occupied") { return false; } }
        var a = Race(f.Member1); var b = Race(f.Member2); gate.SetResult(); var winners = await Task.WhenAll(a, b);
        Assert.Single(winners, x => x); await using var check = f.Db();
        Assert.Equal(1, await check.Allocations.CountAsync(x => x.AssetId == asset && x.State == "Hold"));
        Assert.Equal(1, await check.AuditEvents.CountAsync(x => x.AssetId == asset && x.Action == "HoldCreated"));
    }

    [Fact]
    public async Task Different_assets_and_adjacent_buffered_intervals_succeed()
    {
        await ClearHolds(); var a = await f.Asset(); var b = await f.Asset();
        await Hold(f.Member1, DatabaseFixture.Interval(a)); await Hold(f.Member2, DatabaseFixture.Interval(b));
        await Hold(f.Member2, DatabaseFixture.Interval(a, startHour: 13, endHour: 14));
        await using var db = f.Db(); Assert.Equal(3, await db.Allocations.CountAsync(x => (x.AssetId == a || x.AssetId == b) && x.State == "Hold"));
    }

    [Theory]
    [InlineData(10, 12)]
    [InlineData(9, 14)]
    [InlineData(11, 12)]
    [InlineData(12, 13)]
    public async Task Identical_containment_partial_and_buffer_collisions_fail(int start, int end)
    {
        await ClearHolds(); var a = await f.Asset(); await Hold(f.Member1, DatabaseFixture.Interval(a));
        var e = await Assert.ThrowsAsync<DomainError>(() => Hold(f.Member2, DatabaseFixture.Interval(a, startHour: start, endHour: end)));
        Assert.Equal("occupied", e.Code);
    }

    [Fact]
    public async Task Maintenance_conflicts_with_holds()
    {
        await ClearHolds(); var a = await f.Asset(); var r = DatabaseFixture.Interval(a); await Hold(f.Member1, r);
        await using var db = f.Db(); await Assert.ThrowsAsync<DomainError>(() => new AllocationService(db).Maintenance(f.Manager, DatabaseFixture.Key(), r, "Service lens"));
    }

    [Theory]
    [InlineData("NULL")]
    [InlineData("'empty'::tstzrange")]
    [InlineData("tstzrange(NULL, '2027-01-01', '[)')")]
    [InlineData("tstzrange('2026-01-01', NULL, '[)')")]
    [InlineData("tstzrange('-infinity', '2027-01-01', '[)')")]
    [InlineData("tstzrange('2026-01-01', 'infinity', '[)')")]
    [InlineData("tstzrange('2026-01-01', '2027-01-01', '[]')")]
    public async Task Raw_inserts_reject_null_unbounded_infinite_and_wrong_bounds(string range)
    {
        var a = await f.Asset(); await using var c = new NpgsqlConnection(f.Connection); await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
          INSERT INTO "Allocations" ("Id","AssetId","MemberId","State","Start","End","Occupied","ExpiresAt","PolicyJson","CreatedAt")
          VALUES (@id,@asset,@member,'Hold','2026-01-01','2027-01-01',{range},clock_timestamp()+interval '5 minutes',jsonb_build_object(),clock_timestamp())
          """, c);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid()); cmd.Parameters.AddWithValue("asset", a); cmd.Parameters.AddWithValue("member", f.Member1);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync()); Assert.Contains(ex.SqlState, new[] { "23502", "23514" });
    }

    [Fact]
    public async Task Raw_valid_duplicate_insert_is_rejected_by_exclusion_constraint()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a)); await using var db = f.Db();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO "Allocations" ("Id","AssetId","MemberId","State","Start","End","Occupied","ExpiresAt","PolicyJson","CreatedAt")
          SELECT {Guid.NewGuid()},"AssetId","MemberId",'Hold',"Start","End","Occupied","ExpiresAt","PolicyJson","CreatedAt" FROM "Allocations" WHERE "Id"={h.Id}
          """)); Assert.Equal("23P01", ex.SqlState);
    }

    [Fact]
    public async Task Expired_hold_is_reused_without_a_cleanup_worker()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a));
        await using (var db = f.Db()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Allocations\" SET \"ExpiresAt\"=clock_timestamp() WHERE \"Id\"={h.Id}");
        var error = await Assert.ThrowsAsync<DomainError>(() => Change(f.Member1, h.Id, "confirm")); Assert.Equal("hold_expired", error.Code);
        var replacement = await Hold(f.Member2, DatabaseFixture.Interval(a)); Assert.NotEqual(h.Id, replacement.Id);
        await using var check = f.Db(); Assert.Equal("Expired", (await check.Allocations.FindAsync(h.Id))!.State);
    }

    [Fact]
    public async Task Confirmation_waiting_on_lock_samples_time_after_expiry()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a));
        await using var blocker = new NpgsqlConnection(f.Connection); await blocker.OpenAsync(); await using var tx = await blocker.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM \"Assets\" WHERE \"Id\"=@id FOR UPDATE", blocker, tx)) { lockCmd.Parameters.AddWithValue("id", a); await lockCmd.ExecuteNonQueryAsync(); }
        // The request begins before expiry, then waits across it on the asset guard.
        await using (var expiry = new NpgsqlCommand("UPDATE \"Allocations\" SET \"ExpiresAt\"=clock_timestamp()+interval '1 second' WHERE \"Id\"=@id", blocker, tx)) { expiry.Parameters.AddWithValue("id", h.Id); await expiry.ExecuteNonQueryAsync(); }
        var waiting = Change(f.Member1, h.Id, "confirm");
        await using var observer = new NpgsqlConnection(f.Connection); await observer.OpenAsync();
        var waited = false;
        for (var i = 0; i < 200; i++)
        {
            await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%Assets%FOR UPDATE%')", observer);
            if ((bool)(await cmd.ExecuteScalarAsync())!) { waited = true; break; }
            await Task.Delay(10);
        }
        Assert.True(waited, "Observed a real PostgreSQL lock waiter before release");
        await using (var expiryWait = new NpgsqlCommand("SELECT pg_sleep(GREATEST(0,EXTRACT(EPOCH FROM (\"ExpiresAt\"-clock_timestamp())))) FROM \"Allocations\" WHERE \"Id\"=@id", blocker, tx)) { expiryWait.Parameters.AddWithValue("id", h.Id); await expiryWait.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
        var error = await Assert.ThrowsAsync<DomainError>(() => waiting); Assert.Equal("hold_expired", error.Code);
        await using var check = f.Db(); Assert.Null((await check.Allocations.FindAsync(h.Id))!.Reference);
    }

    [Fact]
    public async Task Concurrent_duplicate_keys_and_lost_response_recovery_return_one_effect()
    {
        await ClearHolds(); var a = await f.Asset(); var r = DatabaseFixture.Interval(a); var key = DatabaseFixture.Key();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AllocationResult> Run() { await gate.Task; return await Hold(f.Member1, r, key); }
        var first = Run(); var second = Run(); gate.SetResult(); var result = await Task.WhenAll(first, second);
        Assert.Equal(JsonSerializer.Serialize(result[0]), JsonSerializer.Serialize(result[1])); // drop both responses, recover through a new service/context
        Assert.Equal(JsonSerializer.Serialize(result[0]), JsonSerializer.Serialize(await Hold(f.Member1, r, key)));
        await using var db = f.Db(); Assert.Equal(1, await db.Allocations.CountAsync(x => x.AssetId == a)); Assert.Equal(1, await db.Idempotency.CountAsync(x => x.Key == key));
        var ex = await Assert.ThrowsAsync<DomainError>(() => Hold(f.Member1, DatabaseFixture.Interval(a, day: 6), key)); Assert.Equal("key_reused", ex.Code);
    }

    [Fact]
    public async Task Member_limit_serializes_across_assets()
    {
        await ClearHolds(); var a = await f.Asset(); var b = await f.Asset(); var c = await f.Asset(); await Hold(f.Member1, DatabaseFixture.Interval(a));
        async Task<bool> Attempt(Guid id) { try { await Hold(f.Member1, DatabaseFixture.Interval(id)); return true; } catch (DomainError e) when (e.Code == "hold_limit") { return false; } }
        var results = await Task.WhenAll(Attempt(b), Attempt(c)); Assert.Single(results, x => x);
    }

    [Fact]
    public async Task Failed_atomic_reschedule_preserves_booking_and_audit()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a)); await Change(f.Member1, h.Id, "confirm");
        var other = await Hold(f.Member2, DatabaseFixture.Interval(a, day: 6)); await Change(f.Member2, other.Id, "confirm");
        var r = DatabaseFixture.Interval(a, day: 6);
        await Assert.ThrowsAsync<DomainError>(() => Change(f.Member1, h.Id, "reschedule", new("Try new dates", r.Start, r.End)));
        await using var db = f.Db(); var original = await db.Allocations.FindAsync(h.Id); Assert.Equal(h.Start, original!.Start); Assert.Equal("Confirmed", original.State);
        Assert.False(await db.AuditEvents.AnyAsync(x => x.AllocationId == h.Id && x.Action == "reschedule"));
    }

    [Fact]
    public async Task Cancellation_racing_confirmation_cannot_resurrect_a_booking()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a));
        async Task Try(string action) { try { await Change(f.Member1, h.Id, action); } catch (DomainError e) when (e.Code == "state_conflict") { } }
        await Task.WhenAll(Try("confirm"), Try("cancel"));
        await using var db = f.Db(); var state = (await db.Allocations.FindAsync(h.Id))!.State; Assert.Contains(state, new[] { "Confirmed", "Cancelled" });
        if (state == "Confirmed") await Change(f.Member1, h.Id, "cancel");
        await Assert.ThrowsAsync<DomainError>(() => Change(f.Member1, h.Id, "confirm"));
    }

    [Fact]
    public async Task Physical_custody_overdue_return_and_inspection_are_conservative()
    {
        await ClearHolds(); var asset = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(asset)); await Change(f.Member1, h.Id, "confirm");
        // Fixture places the scheduled interval around real DB time; production has no test clock.
        await using (var db = f.Db()) await db.Database.ExecuteSqlInterpolatedAsync($"""
          WITH t AS MATERIALIZED (SELECT clock_timestamp() AS instant)
          UPDATE "Allocations" SET "Start"=t.instant-interval '1 hour', "End"=t.instant+interval '1 hour',
          "Occupied"=tstzrange(t.instant-interval '1 hour',t.instant+interval '2 hours','[)') FROM t WHERE "Id"={h.Id}
          """);
        await Change(f.Manager, h.Id, "pickup", manager: true);
        await Assert.ThrowsAsync<DomainError>(() => Change(f.Manager, h.Id, "pickup", manager: true));
        await using (var db = f.Db())
        {
            var e = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
              INSERT INTO "Checkouts" ("Id","AllocationId","AssetId","PickedUpAt") VALUES ({Guid.NewGuid()},{h.Id},{asset},clock_timestamp())
              """)); Assert.Equal("23505", e.SqlState);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
              UPDATE "Allocations" SET "End"="Start"+interval '30 minutes' WHERE "Id"={h.Id}
              """);
        }
        var error = await Assert.ThrowsAsync<DomainError>(() => Hold(f.Member2, DatabaseFixture.Interval(asset, day: 8))); Assert.Equal("physical_custody", error.Code);
        await Change(f.Manager, h.Id, "return", new("Returned with cracked housing", Condition: "Damaged", Accessories: "All included accessories checked"), true);
        await using (var db = f.Db()) { Assert.Equal("Inspection", (await db.Assets.FindAsync(asset))!.State); Assert.NotNull((await db.Checkouts.SingleAsync(x => x.AllocationId == h.Id)).ReturnedAt); }
        await using (var db = f.Db()) await new AllocationService(db).AssetChange(f.Manager, asset, "inspect", DatabaseFixture.Key(), new("Repaired and inspected"));
        await using var check = f.Db(); Assert.Equal("Ready", (await check.Assets.FindAsync(asset))!.State); Assert.True((await check.Assets.FindAsync(asset))!.ReadyAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Retirement_requires_explicit_resolution_of_future_commitments()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a)); await Change(f.Member1, h.Id, "confirm");
        await using var db = f.Db(); var e = await Assert.ThrowsAsync<DomainError>(() => new AllocationService(db).AssetChange(f.Manager, a, "retire", DatabaseFixture.Key(), new("End of service"))); Assert.Equal("commitments", e.Code);
    }

    [Fact]
    public async Task Business_conflict_outcome_is_durable_even_after_equipment_becomes_free()
    {
        await ClearHolds(); var a = await f.Asset(); var r = DatabaseFixture.Interval(a); var h = await Hold(f.Member1, r);
        var key = DatabaseFixture.Key(); await Assert.ThrowsAsync<DomainError>(() => Hold(f.Member2, r, key));
        await Change(f.Member1, h.Id, "release");
        var repeated = await Assert.ThrowsAsync<DomainError>(() => Hold(f.Member2, r, key)); Assert.Equal("occupied", repeated.Code);
        await Hold(f.Member2, r, DatabaseFixture.Key());
        await using var db = f.Db(); Assert.Equal(1, await db.Idempotency.CountAsync(x => x.Key == key));
    }

    [Fact]
    public async Task Early_return_retains_buffered_occupancy_and_audit_is_append_only()
    {
        await ClearHolds(); var asset = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(asset)); await Change(f.Member1, h.Id, "confirm");
        await using (var db = f.Db()) await db.Database.ExecuteSqlInterpolatedAsync($"""
          WITH t AS MATERIALIZED (SELECT clock_timestamp() AS instant)
          UPDATE "Allocations" SET "Start"=t.instant-interval '1 hour', "End"=t.instant+interval '1 hour',
          "Occupied"=tstzrange(t.instant-interval '1 hour',t.instant+interval '2 hours','[)') FROM t WHERE "Id"={h.Id}
          """);
        await Change(f.Manager, h.Id, "pickup", manager: true);
        await using var check = f.Db(); var occupancy = (await check.Allocations.AsNoTracking().SingleAsync(x => x.Id == h.Id)).Occupied;
        await Change(f.Manager, h.Id, "return", new("Early return, inspected", Condition: "Good", Accessories: "All checked"), true);
        Assert.Equal(occupancy, (await check.Allocations.AsNoTracking().SingleAsync(x => x.Id == h.Id)).Occupied);
        Assert.Equal(0, await check.Checkouts.CountAsync(x => x.AssetId == asset && x.ReturnedAt == null));
        await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AuditEvents\" WHERE \"AllocationId\"={h.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Allocations\" SET \"State\"='Confirmed' WHERE \"Id\"={h.Id}"));
    }

    [Fact]
    public async Task Member_cannot_change_accepted_policy_and_new_policy_does_not_rewrite_it()
    {
        await ClearHolds(); var a = await f.Asset(); var h = await Hold(f.Member1, DatabaseFixture.Interval(a)); await Change(f.Member1, h.Id, "confirm");
        await using var db = f.Db(); var original = (await db.Allocations.AsNoTracking().SingleAsync(x => x.Id == h.Id)).PolicyJson;
        await new AllocationService(db).Metadata(f.Manager, "policy-test", DatabaseFixture.Key(), new { change = 5 }, true, (p, _) => { db.Policies.Attach(p); p.LeadMinutes += 5; p.Version++; return Task.FromResult<object>(p); });
        Assert.Equal(original, (await db.Allocations.AsNoTracking().SingleAsync(x => x.Id == h.Id)).PolicyJson);
    }

    [Fact]
    public async Task Inspection_cannot_shorten_existing_return_turnaround()
    {
        var asset = await f.Asset(); await using var db = f.Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Assets\" SET \"State\"='Inspection', \"ReadyAt\"=clock_timestamp()+interval '2 hours' WHERE \"Id\"={asset}");
        var original = (await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset)).ReadyAt;
        await new AllocationService(db).AssetChange(f.Manager, asset, "inspect", DatabaseFixture.Key(), new("Repaired and safety checked"));
        Assert.Equal(original, (await db.Assets.AsNoTracking().SingleAsync(x => x.Id == asset)).ReadyAt);
    }
}
