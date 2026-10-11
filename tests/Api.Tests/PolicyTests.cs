using Fieldroom;
using Xunit;

namespace Api.Tests;

public class PolicyTests
{
    [Theory]
    [InlineData("2026-09-27T02:30")]
    [InlineData("2027-04-04T02:30")]
    [InlineData("2026-10-20T10:00Z")]
    public void Ambiguous_missing_and_offset_local_times_are_rejected(string local) => Assert.Throws<DomainError>(() => PolicyRules.Resolve(local, "Pacific/Auckland"));

    [Fact]
    public void Auckland_wall_time_resolves_to_utc() => Assert.Equal(new DateTime(2026, 10, 19, 21, 0, 0, DateTimeKind.Utc), PolicyRules.Resolve("2026-10-20T10:00", "Pacific/Auckland"));

    [Fact]
    public void Exact_expiry_is_expired()
    {
        var t = DateTime.UtcNow; Assert.True(States.IsExpired(t, t)); Assert.False(States.IsExpired(t.AddTicks(10), t));
    }

    [Theory]
    [InlineData("Returned")]
    [InlineData("Cancelled")]
    [InlineData("CheckedOut")]
    [InlineData("Expired")]
    public void Terminal_or_in_custody_cannot_be_cancelled(string state) => Assert.False(States.CanCancel(state));

    [Fact]
    public void Opening_and_duration_boundaries()
    {
        var p = new LendingPolicy { LeadMinutes = 0 }; var now = PolicyRules.Resolve("2026-10-20T09:00", p.TimeZone);
        PolicyRules.Interval("2026-10-20T09:00", "2026-10-20T09:30", p, now);
        Assert.Throws<DomainError>(() => PolicyRules.Interval("2026-10-20T08:59", "2026-10-20T10:00", p, now.AddDays(-1)));
        Assert.Throws<DomainError>(() => PolicyRules.Interval("2026-10-20T09:00", "2026-10-20T18:01", p, now));
        Assert.Throws<DomainError>(() => PolicyRules.Interval("2026-10-20T09:00", "2026-10-20T09:29", p, now));
        Assert.Throws<DomainError>(() => PolicyRules.Interval("2026-10-20T09:00", "2026-10-28T09:00", p, now));
    }
}
