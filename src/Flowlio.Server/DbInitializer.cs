using Flowlio.Infrastructure.Identity;
using Flowlio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flowlio.Server;

/// <summary>Applies pending migrations and seeds a demo family account on startup (development convenience).</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var config = sp.GetRequiredService<IConfiguration>();
        var email = config["Seed:DemoEmail"];
        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is null)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Rodina Flowlio",
            };
            await users.CreateAsync(user, password);
        }
    }
}
