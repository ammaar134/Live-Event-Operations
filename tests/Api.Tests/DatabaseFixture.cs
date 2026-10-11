using Fieldroom;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Api.Tests;

[CollectionDefinition("postgres")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string admin = "";
    private readonly string name = "fieldroom_test_" + Guid.NewGuid().ToString("N");
    public string Connection { get; private set; } = "";
    public WebApplicationFactory<Program> App { get; private set; } = null!;
    public string Member1 { get; private set; } = "";
    public string Member2 { get; private set; } = "";
    public string Manager { get; private set; } = "";
    public string Password { get; } = "Test!" + Guid.NewGuid().ToString("N") + "a";
    public LibraryDb Db() => new(new DbContextOptionsBuilder<LibraryDb>().UseNpgsql(Connection).Options);
    public async Task InitializeAsync()
    {
        admin = Environment.GetEnvironmentVariable("TEST_POSTGRES") ?? "";
        if (admin == "") { container = new PostgreSqlBuilder("postgres:18.6-bookworm").Build(); await container.StartAsync(); admin = container.GetConnectionString(); }
        await using var connection = new NpgsqlConnection(admin); await connection.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection); await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(admin) { Database = name }; Connection = cs.ConnectionString;
        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Library"] = Connection, ["BootstrapPassword"] = null, ["AutoMigrate"] = "false" })));
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDb>();
        // Proves an empty install AND the upgrade from baseline schema to guards.
        await db.GetService<IMigrator>().MigrateAsync("20261011055442_InitialLibrary");
        await db.Database.MigrateAsync();
        await Seed.Run(scope.ServiceProvider, Password);
        await Seed.Run(scope.ServiceProvider, Password);
        Assert.Equal(15, await db.Products.CountAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Member>>();
        Manager = (await users.FindByEmailAsync("desk@fieldroom.example"))!.Id;
        foreach (var email in new[] { "alex@example.test", "sam@example.test" })
        {
            var u = new Member { UserName = email, Email = email, DisplayName = email.Split('@')[0] };
            Assert.True((await users.CreateAsync(u, Password)).Succeeded);
            if (Member1 == "") Member1 = u.Id; else Member2 = u.Id;
        }
    }
    public async Task<Guid> Asset()
    {
        await using var db = Db(); var a = new Asset { ProductId = Guid.Parse("10000000-0000-0000-0000-000000000001"), Tag = "TEST-" + Guid.NewGuid().ToString("N") }; db.Assets.Add(a); await db.SaveChangesAsync(); return a.Id;
    }
    public static string Key() => Guid.NewGuid().ToString();
    public static IntervalRequest Interval(Guid id, int day = 5, int startHour = 10, int endHour = 12)
    {
        var local = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Pacific/Auckland").Date.AddDays(day);
        return new(id, local.AddHours(startHour).ToString("yyyy-MM-dd'T'HH:mm"), local.AddHours(endHour).ToString("yyyy-MM-dd'T'HH:mm"));
    }
    public async Task DisposeAsync()
    {
        await App.DisposeAsync(); NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(admin); await c.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", c); await drop.ExecuteNonQueryAsync();
        if (container != null) await container.DisposeAsync();
    }
}
