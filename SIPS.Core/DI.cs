global using static SIPS.Core.Constants;
global using SIPS.Core.Interfaces;
using SIPS.Adapter;
using SIPS.Core.Services;
using SIPS.ISO20022.Interfaces;
using SIPS.ISO20022.Adapters;
using SIPS.Core.Services.Verification;
using SIPS.Core.Services.ISOParsers;
using SIPS.Core.Services.Callback;
using SIPS.Core.Services.Responses;
using SIPS.Core.Services.Persistence;
using SIPS.Core.Services.Correlation;
using SIPS.PostgreSQL;
using SIPS.XMLDsig.Xades;
using SIPS.XMLDsig.Xades.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SIPS.Core.Extensions;
using SIPS.Core.Workers;
using SIPS.Core.Models;
using SIPS.Core.Services.Abstractions;
using SIPS.Core.Services.Implementations;
namespace SIPS.Core;
public static class DI
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddSingleton<ICertificateDownloadService, CertificateDownloadService>();
        services.AddScoped<IIncoming, Incoming>();

        // ISO20022 helpers/adapters
        services.AddSingleton<IPaymentStatusRequestBuilder, PaymentStatusRequestBuilderAdapter>();

        services.AddSingleton<ISignatureService, SignatureService>();
        services.AddSingleton<ICorrelationService, CorrelationService>();
        services.AddSingleton<IPaymentRequestParser, PaymentRequestParser>();
        services.AddSingleton<IPaymentStatusRequestParser, PaymentStatusRequestParser>();
        services.AddSingleton<IPayeeVerificationRequestParser, PayeeVerificationRequestParser>();
        services.AddSingleton<IReturnPaymentRequestParser, ReturnPaymentRequestParser>();
        services.AddSingleton<IPaymentStatusReportParser, PaymentStatusReportParser>();
        services.AddSingleton<ICallbackClient, CallbackClient>();
        services.AddSingleton<IResponseFactory, ResponseFactory>();
        // new helper services
        services.AddSingleton<IInboundMessageService, InboundMessageService>();
        services.AddSingleton<ICallbackOrchestrator, CallbackOrchestrator>();
        services.AddScoped<IISOMessageService, ISOMessageService>();
        services.AddSingleton<ISipsRequestSender, SipsRequestSender>();
        services.AddScoped<IPersistenceGateway, PersistenceGateway>();

        services.AddScoped<IIncomingVerificationHandler, IncomingVerificationHandler>();
        services.AddScoped<IIncomingTransactionHandler, IncomingTransactionHandler>();
        services.AddScoped<IIncomingTransactionStatusHandler, IncomingTransactionStatusHandler>();
        services.AddScoped<IIncomingReturnTransactionHandler, IncomingReturnTransactionHandler>();
        services.AddScoped<IIncomingPaymentStatusReportHandler, IncomingPaymentStatusReportHandler>();

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