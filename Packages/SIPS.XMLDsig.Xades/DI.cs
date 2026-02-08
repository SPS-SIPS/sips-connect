using Microsoft.Extensions.DependencyInjection;

namespace SIPS.XMLDsig.Xades;
public static class DI
{
    public static IServiceCollection AddXades(this IServiceCollection services)
    {
        services.AddSingleton<ICertificateService, CertificateService>();
        services.AddSingleton<INativeSigner, NativeSigner>();
        services.AddSingleton<INativeVerifier, NativeVerifier>();
        return services;
    }
}