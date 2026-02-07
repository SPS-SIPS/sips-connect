using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SIPS.PostgreSQL.Persistence;

public sealed class StorageBrokerFactory : IDesignTimeDbContextFactory<StorageBroker>
{
    public StorageBroker CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0 ? args[0] 
            : Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
            ?? Environment.GetEnvironmentVariable("SIPS_DB") 
            ?? "Host=localhost;Database=SIPS;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<StorageBroker>();
        optionsBuilder.UseNpgsql(connectionString).UseLowerCaseNamingConvention();

        return new StorageBroker(optionsBuilder.Options);
    }
}