using Microsoft.Extensions.DependencyInjection;
using SIPS.Adapter.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SIPS.Adapter;
public static class DI
{
    public static IServiceCollection AddJsonAdapter(this IServiceCollection services)
    {
        // Register default options only if the user hasn't configured one already
        services.TryAddSingleton(sp => new JsonAdapterOptions());
        services.AddSingleton<IJsonAdapter, JsonAdapter>();
        return services;
    }
}