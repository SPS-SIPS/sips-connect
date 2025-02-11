namespace SIPS.PostgreSQL.Interfaces;

public interface IStorageBrokerInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SeedAsync(CancellationToken cancellationToken = default);
}