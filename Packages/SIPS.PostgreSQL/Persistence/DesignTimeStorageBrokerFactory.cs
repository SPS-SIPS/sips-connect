using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SIPS.PostgreSQL.Persistence;

public sealed class DesignTimeStorageBrokerFactory : IDesignTimeDbContextFactory<StorageBroker>
{
    public StorageBroker CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StorageBroker>()
            .UseNpgsql("Host=localhost;Database=sips_design;Username=postgres")
            .UseLowerCaseNamingConvention()
            .Options;

        return new StorageBroker(options);
    }
}
