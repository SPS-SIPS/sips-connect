using Microsoft.Extensions.DependencyInjection;
using SIPS.XMLDsig.Xades.Interfaces;
using SIPS.XMLDsig.Xades.Options;
using SIPS.XMLDsig.Xades.Services;

namespace SIPS.XMLDsig.Xades;
public static class DI
{
    public static IServiceCollection AddXades(this IServiceCollection services)
    {
        services.AddSingleton<ICertificateService>(sp =>
        {
            var options = sp.GetRequiredService<XadesOptions>();
            return options.WithoutPKI
                ? new NoOpCertificateService(sp.GetRequiredService<ILogger<NoOpCertificateService>>())
                : new CertificateService(options);
        });

        services.AddSingleton<INativeSigner, NativeSigner>();
        services.AddSingleton<INativeVerifier, NativeVerifier>();
        return services;
    }
}