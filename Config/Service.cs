using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SIPS.Adapter.Models;
using SIPS.Connect.Filters;
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

        services.AddSwaggerGen(o =>
         {
             o.SwaggerDoc("v1", new() { Title = "SIPS Connect Platform API", Version = "v1" });
             o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
             {
                 Description = "Please provide a JWT Token To get Authorized",
                 Name = "Authorization",
                 Type = SecuritySchemeType.Http,
                 BearerFormat = "JWT",
                 In = ParameterLocation.Header,
                 Scheme = "Bearer",
             });
             o.AddSecurityRequirement(new OpenApiSecurityRequirement
             {
                {
                    new OpenApiSecurityScheme
                        {
                            Name = "Bearer",
                            Type = SecuritySchemeType.Http,
                            In = ParameterLocation.Header,
                            BearerFormat = "JWT",
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            },
                        },
                        Array.Empty<string>()
                }
             });
             o.OperationFilter<AuthorizeCheckOperationFilter>();
         });

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

        var corsPolicy = new CorsPolicies();
        configuration.Bind(nameof(CorsPolicies), corsPolicy);

        services.AddCors(options =>
        {
            options.AddPolicy("default", builder =>
            {
                if (corsPolicy.Origins != null && corsPolicy.Origins.Length != 0)
                {
                    if (corsPolicy.Origins.Contains("*"))
                    {
                        builder.AllowAnyOrigin();
                    }
                    else
                    {
                        builder.WithOrigins(corsPolicy.Origins).AllowCredentials();
                    }
                }
                else
                {
                    builder.AllowAnyOrigin();
                }

                builder
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddSingleton<IInterfaceHttpClient, InterfaceHttpClient>(sp =>
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<InterfaceHttpClient>>();
    var client = clientFactory.CreateClient();
    return new InterfaceHttpClient(logger, client);
});

        services.AddAuthentication(options =>
       {
           options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
           options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
       })
       .AddJwtBearer(options =>
       {
           var host = configuration["Keycloak:Realm:Host"];
           var protocol = configuration["Keycloak:Realm:Protocol"];
           var realm = configuration["Keycloak:Realm:Name"];
           var audience = configuration["Keycloak:Realm:Audience"];
           var validateIssuer = bool.Parse(configuration["Keycloak:Realm:ValidateIssuer"] ?? "true");

           var authority = $"{protocol}://{host}/realms/{realm}";

           options.Authority = authority;
           options.Audience = audience;
           options.RequireHttpsMetadata = false;
           options.MapInboundClaims = false;

           options.TokenValidationParameters = new TokenValidationParameters
           {
               ValidateIssuer = validateIssuer,
               ValidIssuer = authority,
               ValidateAudience = true,
               ValidAudience = audience,
               ValidateLifetime = true,
               RoleClaimType = ClaimTypes.Role,
           };

           options.Events = new JwtBearerEvents
           {
               OnTokenValidated = context =>
               {
                   var realmAccessClaim = context.Principal?.FindFirst("realm_access");
                   if (realmAccessClaim is not null)
                   {
                       using var doc = JsonDocument.Parse(realmAccessClaim.Value);
                       if (doc.RootElement.TryGetProperty("roles", out var rolesElement)
                           && rolesElement.ValueKind == JsonValueKind.Array)
                       {
                           var roles = rolesElement.EnumerateArray()
                                                   .Select(r => r.GetString())
                                                   .Where(r => !string.IsNullOrEmpty(r))
                                                   .ToList();

                           if (context.Principal?.Identity is ClaimsIdentity identity)
                           {
                               foreach (var role in roles)
                               {
                                   if (role == null)
                                   {
                                       continue;
                                   }
                                   identity.AddClaim(new Claim(ClaimTypes.Role, role));
                               }
                           }
                       }
                   }

                   return Task.CompletedTask;
               }
           };
       });
        services.AddCore(configuration);
    }
}