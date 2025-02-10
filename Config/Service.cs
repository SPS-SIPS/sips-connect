using SIPS.Adapter.Models;
using SIPS.Core;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.Connect.Config;
public static class DI
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            // Bind Endpoints configuration
            var options = new JsonAdapterOptions();
            configuration.GetSection("Endpoints").Bind(options.Endpoints);
            // Bind DateFormats configuration
            var dateFormats = configuration.GetSection("DateFormats").Get<string[]>();
            options.DateFormats = dateFormats ?? ["yyyy-MM-dd"];
            return options;
        });

        services.AddSingleton(sp =>
        {
            var options = new CoreOptions();
            configuration.GetSection("Core").Bind(options);
            return options;
        });
        services.AddSingleton(sp =>
        {
            var options = new XadesOptions();
            configuration.GetSection("Xades").Bind(options);
            return options;
        });
        services.AddSingleton(sp =>
        {
            var options = new ISO20022Options();
            configuration.GetSection("ISO20022").Bind(options);
            return options;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = new Action<ProblemDetailsContext>(context =>
            {
                var traceId = context.HttpContext.TraceIdentifier;
                var problemDetails = context.ProblemDetails;
            });
        });

        services.AddSingleton<IRepositoryHttpClient, RepositoryHttpClient>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<RepositoryHttpClient>>();
            var client = clientFactory.CreateClient();
            var baseUrl = configuration["Core:BaseUrl"] ?? throw new ArgumentNullException("Core:BaseUrl is required in appSettings.json");
            client.BaseAddress = new Uri(baseUrl);
            return new RepositoryHttpClient(logger, client);
        });

        services.AddSingleton<IInterfaceHttpClient, InterfaceHttpClient>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<InterfaceHttpClient>>();
            var client = clientFactory.CreateClient();
            return new InterfaceHttpClient(logger, client);
        });

        services.AddCore(configuration);
    }
}