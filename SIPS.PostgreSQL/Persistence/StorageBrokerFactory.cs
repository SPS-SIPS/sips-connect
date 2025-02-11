using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SIPS.PostgreSQL.Persistence;

public sealed class StorageBrokerFactory : IDesignTimeDbContextFactory<StorageBroker>
{
    public StorageBroker CreateDbContext(string[] args)
    {
        if (args[0] == null)
        {
            throw new ArgumentException("You need to add db connection string on the first arg");
        }

        var optionsBuilder = new DbContextOptionsBuilder<StorageBroker>();
        optionsBuilder.UseNpgsql(args[0]).UseLowerCaseNamingConvention();

        return new StorageBroker(optionsBuilder.Options);
    }
}