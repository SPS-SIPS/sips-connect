using SIPS.PostgreSQL.Gateway;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SIPS.PostgreSQL;
public static class DI
{
    public static IServiceCollection AddPostgreSQL(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("db");
        connectionString = connectionString?.Replace("{{PASSWORD}}", Environment.GetEnvironmentVariable("POSTGRESQL_PASSWORD"));

        services.AddDbContext<StorageBroker>((sp, options) =>
        {
            options.UseNpgsql(connectionString).UseLowerCaseNamingConvention();
        });

        services.AddScoped<IStorageBroker>(provider => provider.GetService<StorageBroker>()!);

        services.AddScoped<IIncomingRecorder, IncomingRecorder>();
        services.AddScoped<IStorageBrokerInitializer, StorageBrokerInitializer>();
        return services;
    }
}