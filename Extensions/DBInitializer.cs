using SIPS.PostgreSQL.Interfaces;

namespace SIPS.Connect.Extensions;
public static class InitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var initializer = services.GetRequiredService<IStorageBrokerInitializer>();
        await initializer.InitializeAsync();
        await initializer.SeedAsync();
    }
}