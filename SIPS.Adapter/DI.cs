using Microsoft.Extensions.DependencyInjection;
using SIPS.Adapter.Models;

namespace SIPS.Adapter;
public static class DI
{
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services)
    {
        // Register default options; callers can replace this with their own configured instance
        services.AddSingleton(new JsonAdapterOptions());
        services.AddSingleton<IJsonAdapter, JsonAdapter>();
        return services;
    }
}