using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Flowlio.Infrastructure.Persistence;

/// <summary>Design-time factory so <c>dotnet ef</c> can build the context without the web host.</summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWLIO_DB")
            ?? "Host=localhost;Port=5432;Database=flowlio;Username=flowlio;Password=flowlio_dev";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql(connectionString);
        options.UseOpenIddict();

        return new ApplicationDbContext(options.Options);
    }
}
