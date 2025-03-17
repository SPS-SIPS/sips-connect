global using static SIPS.Core.Constants;
global using SIPS.Core.Interfaces;
using SIPS.Adapter;
using SIPS.Core.Services;
using SIPS.ISO20022.Interfaces;
using SIPS.PostgreSQL;
using SIPS.XMLDsig.Xades;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIPS.Core.Extensions;
using SIPS.Core.Workers;
using SIPS.Core.Models;
namespace SIPS.Core;
public static class DI
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<ICertificateDownloadService, CertificateDownloadService>();
        services.AddScoped<IIncoming, Incoming>();

        services.AddScoped<IIncomingVerificationHandler, IncomingVerificationHandler>();
        services.AddScoped<IIncomingTransactionHandler, IncomingTransactionHandler>();
        services.AddScoped<IIncomingTransactionStatusHandler, IncomingTransactionStatusHandler>();
        services.AddScoped<IIncomingReturnTransactionHandler, IncomingReturnTransactionHandler>();

        services.AddScoped<IOutgoingVerificationHandler, OutgoingVerificationHandler>();
        services.AddScoped<IOutgoingTransactionStatusHandler, OutgoingTransactionStatusHandler>();
        services.AddScoped<IOutgoingTransactionHandler, OutgoingTransactionHandler>();
        services.AddScoped<IOutgoingReturnTransactionHandler, OutgoingReturnTransactionHandler>();

        services.AddJsonAdapter();
        services.AddXades();
        services.AddPostgreSQL(configuration);

        var expression = configuration.GetSection("Core:SAFExpression").Value;
        var timeZoneInfo = configuration.GetSection("Core:SAFTimeZoneInfo").Value;

        services.AddCronJob<SAFWorker>(new JobConfiguration { Expression = expression, TimeZoneInfo = timeZoneInfo });

        return services;
    }
}