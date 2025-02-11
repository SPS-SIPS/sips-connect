using Microsoft.Extensions.DependencyInjection;

namespace SIPS.Adapter;
public static class DI
{
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IJsonAdapter, JsonAdapter>();
        return services;
    }
}