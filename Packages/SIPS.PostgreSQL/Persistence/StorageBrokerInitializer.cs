using SIPS.PostgreSQL.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SIPS.PostgreSQL.Persistence;

public sealed partial class StorageBrokerInitializer(ILogger<StorageBrokerInitializer> logger, IStorageBroker broker) : IStorageBrokerInitializer
{
    private readonly ILogger<StorageBrokerInitializer> _logger = logger;
    private readonly IStorageBroker _broker = broker;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _broker.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await TrySeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    private async Task TrySeedAsync(CancellationToken cancellationToken = default)
    {
        await _broker.SaveChangesAsync(cancellationToken);
    }
}