using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Clippy.Data;

public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=clippy_db;Username=postgres";

        var serviceProvider = new ServiceCollection()
            .AddOptions<IdentityOptions>()
            .Configure(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .Services
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseApplicationServiceProvider(serviceProvider)
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
