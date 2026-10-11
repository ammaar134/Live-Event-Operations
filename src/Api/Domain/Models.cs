using Microsoft.AspNetCore.Identity;
using NpgsqlTypes;

namespace Fieldroom;

public sealed class Member : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Specifications { get; set; } = "";
    public string Accessories { get; set; } = "";
    public string Image { get; set; } = "camera";
    public List<Asset> Assets { get; set; } = [];
}

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Tag { get; set; } = "";
    public string Condition { get; set; } = "Excellent";
    public string Location { get; set; } = "Fieldroom · 14 Workshop Lane";
    public string State { get; set; } = "Ready";
    public DateTime? ReadyAt { get; set; }
}

public static class States
{
    public static readonly string[] Blocking = ["Hold", "Confirmed", "CheckedOut", "Returned", "Maintenance"];
    public static readonly string[] Booking = ["Confirmed", "CheckedOut", "Returned", "Cancelled", "NoShow"];
    public static bool CanCancel(string state) => state == "Confirmed";
    public static bool IsExpired(DateTime expiry, DateTime decision) => expiry <= decision;
}

public sealed class Allocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public string MemberId { get; set; } = "";
    public Member Member { get; set; } = null!;
    public string State { get; set; } = "Hold";
    public string? Reference { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public NpgsqlRange<DateTime> Occupied { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string PolicyJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class Checkout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllocationId { get; set; }
    public Allocation Allocation { get; set; } = null!;
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public DateTime PickedUpAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public string? Condition { get; set; }
    public string? AccessoryCheck { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public string ActorId { get; set; } = "";
    public Guid? AllocationId { get; set; }
    public Guid? AssetId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed class Idempotency
{
    public string MemberId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Key { get; set; } = "";
    public string Digest { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class LendingPolicy
{
    public int Id { get; set; } = 1;
    public string TimeZone { get; set; } = "Pacific/Auckland";
    public int OpenHour { get; set; } = 9;
    public int CloseHour { get; set; } = 18;
    public int LeadMinutes { get; set; } = 60;
    public int MaximumHours { get; set; } = 168;
    public int HorizonDays { get; set; } = 90;
    public int BufferMinutes { get; set; } = 60;
    public int CancellationMinutes { get; set; } = 120;
    public int HoldSeconds { get; set; } = 300;
    public int HoldLimit { get; set; } = 2;
    public int Version { get; set; } = 1;
}

public sealed class DomainError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class PolicyRules
{
    // Browser sends organization-local wall times, never a browser timezone guess.
    public static DateTime Resolve(string value, string zone)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var local))
            throw new DomainError(400, "invalid_time", "Choose a date and time to the minute.");
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsAmbiguousTime(local) || tz.IsInvalidTime(local))
            throw new DomainError(400, "ambiguous_time", "That local time is skipped or repeated by daylight saving. Choose another time.");
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    public static (DateTime Start, DateTime End) Interval(string start, string end, LendingPolicy p, DateTime now)
    {
        var s = Resolve(start, p.TimeZone);
        var e = Resolve(end, p.TimeZone);
        if (s < now.AddMinutes(p.LeadMinutes)) throw new DomainError(400, "lead_time", $"Pickup needs at least {p.LeadMinutes} minutes' notice.");
        if (e <= s || e - s < TimeSpan.FromMinutes(30) || e - s > TimeSpan.FromHours(p.MaximumHours))
            throw new DomainError(400, "duration", $"Loans must be between 30 minutes and {p.MaximumHours} hours.");
        if (e > now.AddDays(p.HorizonDays)) throw new DomainError(400, "horizon", $"Return must be within {p.HorizonDays} days.");
        var tz = TimeZoneInfo.FindSystemTimeZoneById(p.TimeZone);
        foreach (var instant in new[] { s, e })
        {
            var t = TimeZoneInfo.ConvertTimeFromUtc(instant, tz).TimeOfDay;
            if (t < TimeSpan.FromHours(p.OpenHour) || t > TimeSpan.FromHours(p.CloseHour))
                throw new DomainError(400, "closed", $"Pickup and return hours are {p.OpenHour:00}:00–{p.CloseHour:00}:00 daily, {p.TimeZone}.");
        }
        return (s, e);
    }

    public static void Validate(LendingPolicy p)
    {
        if (p.TimeZone != "Pacific/Auckland" || p.OpenHour < 0 || p.CloseHour > 23 || p.CloseHour <= p.OpenHour ||
            p.LeadMinutes is < 0 or > 10080 || p.MaximumHours is < 1 or > 720 || p.HorizonDays is < 1 or > 365 ||
            p.BufferMinutes is < 0 or > 1440 || p.CancellationMinutes is < 0 or > 10080 ||
            p.HoldSeconds is < 30 or > 900 || p.HoldLimit is < 1 or > 5)
            throw new DomainError(400, "invalid_policy", "Policy values are outside the safe supported limits.");
    }
}
