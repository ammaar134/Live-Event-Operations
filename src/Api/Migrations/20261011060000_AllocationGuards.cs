using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fieldroom.Migrations;

[DbContext(typeof(LibraryDb))]
[Migration("20261011060000_AllocationGuards")]
public sealed class AllocationGuards : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Allocations" ADD CONSTRAINT "valid_occupancy" CHECK (
          "Occupied" IS NOT NULL AND NOT isempty("Occupied")
          AND NOT lower_inf("Occupied") AND NOT upper_inf("Occupied")
          AND lower("Occupied") IS NOT NULL AND upper("Occupied") IS NOT NULL
          AND isfinite(lower("Occupied")) AND isfinite(upper("Occupied"))
          AND lower_inc("Occupied") AND NOT upper_inc("Occupied")
          AND lower("Occupied") < upper("Occupied")
          AND isfinite("Start") AND isfinite("End") AND "Start" < "End"
          AND lower("Occupied") = "Start" AND upper("Occupied") >= "End"
        );
        ALTER TABLE "Allocations" ADD CONSTRAINT "valid_allocation_state" CHECK (
          "State" IN ('Hold','Expired','Released','Confirmed','Cancelled','CheckedOut','Returned','NoShow','Maintenance','MaintenanceEnded')
          AND ("State" <> 'Hold' OR ("ExpiresAt" IS NOT NULL AND isfinite("ExpiresAt")))
          AND (("State" IN ('Confirmed','Cancelled','CheckedOut','Returned','NoShow') AND "Reference" IS NOT NULL)
            OR ("State" NOT IN ('Confirmed','Cancelled','CheckedOut','Returned','NoShow') AND "Reference" IS NULL))
        );
        ALTER TABLE "Allocations" ADD CONSTRAINT "no_overlapping_allocations"
          EXCLUDE USING gist ("AssetId" WITH =, "Occupied" WITH &&)
          WHERE ("State" IN ('Hold','Confirmed','CheckedOut','Returned','Maintenance'));
        ALTER TABLE "Assets" ADD CONSTRAINT "asset_state" CHECK ("State" IN ('Ready','Inspection','Retired'));
        ALTER TABLE "Checkouts" ADD CONSTRAINT "return_check" CHECK (
          isfinite("PickedUpAt") AND ("ReturnedAt" IS NULL OR
          (isfinite("ReturnedAt") AND "ReturnedAt" >= "PickedUpAt" AND "Condition" IS NOT NULL AND "AccessoryCheck" IS NOT NULL))
        );
        ALTER TABLE "Policies" ADD CONSTRAINT "safe_policy" CHECK (
          "Id" = 1 AND "TimeZone" = 'Pacific/Auckland' AND "OpenHour" >= 0 AND "CloseHour" <= 23 AND "CloseHour" > "OpenHour"
          AND "LeadMinutes" BETWEEN 0 AND 10080 AND "MaximumHours" BETWEEN 1 AND 720
          AND "HorizonDays" BETWEEN 1 AND 365 AND "BufferMinutes" BETWEEN 0 AND 1440
          AND "CancellationMinutes" BETWEEN 0 AND 10080 AND "HoldSeconds" BETWEEN 30 AND 900 AND "HoldLimit" BETWEEN 1 AND 5
        );
        ALTER TABLE "AuditEvents" ADD CONSTRAINT "audit_actor" FOREIGN KEY ("ActorId") REFERENCES "AspNetUsers"("Id");
        ALTER TABLE "AuditEvents" ADD CONSTRAINT "audit_asset" FOREIGN KEY ("AssetId") REFERENCES "Assets"("Id");
        ALTER TABLE "AuditEvents" ADD CONSTRAINT "audit_allocation" FOREIGN KEY ("AllocationId") REFERENCES "Allocations"("Id") DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION reject_audit_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Business audit events are append-only'; END $$;
        CREATE TRIGGER audit_append_only BEFORE UPDATE OR DELETE ON "AuditEvents" FOR EACH ROW EXECUTE FUNCTION reject_audit_change();
        CREATE FUNCTION guard_allocation_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW."AssetId" <> OLD."AssetId" OR NEW."MemberId" <> OLD."MemberId" THEN
            RAISE EXCEPTION 'Allocation ownership and asset are immutable';
          END IF;
          IF NEW."State" <> OLD."State" AND NOT (
            (OLD."State" = 'Hold' AND NEW."State" IN ('Expired','Released','Confirmed')) OR
            (OLD."State" = 'Confirmed' AND NEW."State" IN ('Cancelled','CheckedOut','NoShow')) OR
            (OLD."State" = 'CheckedOut' AND NEW."State" = 'Returned') OR
            (OLD."State" = 'Maintenance' AND NEW."State" = 'MaintenanceEnded')
          ) THEN RAISE EXCEPTION 'Illegal allocation lifecycle transition'; END IF;
          IF OLD."State" IN ('Cancelled','Expired','Released','Returned','NoShow','MaintenanceEnded') THEN
            RAISE EXCEPTION 'Terminal allocations are immutable';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER allocation_lifecycle BEFORE UPDATE ON "Allocations" FOR EACH ROW EXECUTE FUNCTION guard_allocation_transition();
        """);

    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TRIGGER allocation_lifecycle ON "Allocations";
        DROP FUNCTION guard_allocation_transition();
        DROP TRIGGER audit_append_only ON "AuditEvents";
        DROP FUNCTION reject_audit_change();
        ALTER TABLE "AuditEvents" DROP CONSTRAINT audit_actor, DROP CONSTRAINT audit_asset, DROP CONSTRAINT audit_allocation;
        ALTER TABLE "Policies" DROP CONSTRAINT safe_policy;
        ALTER TABLE "Checkouts" DROP CONSTRAINT return_check;
        ALTER TABLE "Assets" DROP CONSTRAINT asset_state;
        ALTER TABLE "Allocations" DROP CONSTRAINT valid_occupancy, DROP CONSTRAINT valid_allocation_state, DROP CONSTRAINT no_overlapping_allocations;
        """);
}
