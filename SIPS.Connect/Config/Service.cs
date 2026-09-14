using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SIPS.Adapter.Models;
using SIPS.Connect.Filters;
using SIPS.Connect.Services;
using SIPS.Connect.Services.Internal;
using SIPS.Core;
using SIPS.Core.Interfaces;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.ISO20022.Options;
using SIPS.XMLDsig.Xades.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SIPS.Adapter;
using SIPS.Core.Services.Callback;

namespace SIPS.Connect.Config;
public static class DI
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var options = new JsonAdapterOptions();
            configuration.GetSection("Endpoints").Bind(options.Endpoints);
            var dateFormats = configuration.GetSection("DateFormats").Get<string[]>();
                options.DateFormats = dateFormats ?? new[] { "yyyy-MM-dd" };
            return options;
        });
        services.AddSingleton(sp =>
        {
            var options = new PapssFacingOptions();
            configuration.GetSection(PapssFacingOptions.SectionName).Bind(options);
            ValidatePapssFacing(options, configuration);
            return options;
        });

        services.AddSingleton(sp =>
        {
            var options = new CoreOptions();
            configuration.GetSection("Core").Bind(options);
            
            // Decrypt any encrypted configuration values
            var dataProtectionProvider = sp.GetService<IDataProtectionProvider>();
            var logger = sp.GetRequiredService<ILogger<CoreOptions>>();
            if (dataProtectionProvider != null)
            {
                ConfigurationDecryptor.DecryptOptions(options, dataProtectionProvider, logger);
            }

            return options;
        });

        services.AddSingleton(sp =>
        {
            var options = new AuthenticationSchemeOptions() {
                TimeProvider = TimeProvider.System,
            };
            return options;
        });
        services.AddSingleton(sp =>
        {
            var options = new XadesOptions();
            configuration.GetSection("Xades").Bind(options);
            
            // Decrypt any encrypted configuration values
            var dataProtectionProvider = sp.GetService<IDataProtectionProvider>();
            var logger = sp.GetRequiredService<ILogger<XadesOptions>>();
            if (dataProtectionProvider != null)
            {
                ConfigurationDecryptor.DecryptOptions(options, dataProtectionProvider, logger);
            }

            var papssEnabled = configuration.GetValue<bool>("PapssFacing:Enabled");
            if (papssEnabled && options.WithoutPKI)
                throw new InvalidOperationException("PAPSS cannot start with Xades:WithoutPKI enabled.");
            if (papssEnabled && (!StringComparer.Ordinal.Equals(options.DefaultSignatureMethod, "SHA256withRSA") ||
                options.Algorithms is not { Length: 1 } ||
                !StringComparer.Ordinal.Equals(options.Algorithms[0], "SHA256withRSA")))
                throw new InvalidOperationException("The WP-SIPS signing profile is fixed to SHA256withRSA.");
            if (papssEnabled && options.VerificationWindowMinutes != 100)
                throw new InvalidOperationException("The WP-SIPS verification window is fixed to 100 minutes.");
            
            return options;
        });
        services.AddSingleton(sp =>
        {
            var options = new ISO20022Options();
            configuration.GetSection("ISO20022").Bind(options);
            
            // Decrypt any encrypted configuration values
            var dataProtectionProvider = sp.GetService<IDataProtectionProvider>();
            var logger = sp.GetRequiredService<ILogger<ISO20022Options>>();
            if (dataProtectionProvider != null)
            {
                ConfigurationDecryptor.DecryptOptions(options, dataProtectionProvider, logger);
            }
            
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

        // Check WithoutPKI flag for conditional registration
        var withoutPki = configuration.GetValue<bool>("Xades:WithoutPKI") || configuration.GetValue<bool>("Xades__WithoutPKI");

        if (withoutPki)
        {
            services.AddSingleton<IRepositoryHttpClient, NoOpRepositoryHttpClient>();
        }
        else
        {
            // Broad PKI-On Fail-Fast Validation
            var baseUrl = configuration["Core:BaseUrl"];
            var pubKeyUrl = configuration["Core:PublicKeysRepUrl"];
            var loginUrl = configuration["Core:LoginEndpoint"];
            var username = configuration["Core:Username"];
            var password = configuration["Core:Password"];

            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(baseUrl)) missingFields.Add("Core:BaseUrl");
            if (string.IsNullOrWhiteSpace(pubKeyUrl)) missingFields.Add("Core:PublicKeysRepUrl");
            if (string.IsNullOrWhiteSpace(loginUrl)) missingFields.Add("Core:LoginEndpoint");
            if (string.IsNullOrWhiteSpace(username)) missingFields.Add("Core:Username");
            if (string.IsNullOrWhiteSpace(password)) missingFields.Add("Core:Password");

            if (missingFields.Count != 0)
            {
                throw new InvalidOperationException($"PKI Mode is enabled (WithoutPKI=false) but required Core discovery settings are missing or empty: {string.Join(", ", missingFields)}. Please provide these in appsettings.json or .env.");
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                throw new InvalidOperationException($"PKI Mode is enabled (WithoutPKI=false) but Core:BaseUrl is not a valid absolute URI: '{baseUrl}'. Please check your configuration.");
            }

            if (!Uri.TryCreate(pubKeyUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException($"PKI Mode is enabled (WithoutPKI=false) but Core:PublicKeysRepUrl is not a valid absolute URI: '{pubKeyUrl}'. Please check your configuration.");
            }

            services.AddSingleton<IRepositoryHttpClient, RepositoryHttpClient>(sp =>
            {
                var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var logger = sp.GetRequiredService<ILogger<RepositoryHttpClient>>();
                var client = clientFactory.CreateClient();
                client.BaseAddress = baseUri;
                return new RepositoryHttpClient(logger, client);
            });
        }

        services.AddSingleton<IInterfaceHttpClient, InterfaceHttpClient>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<InterfaceHttpClient>>();
            var client = clientFactory.CreateClient();
            var coreOptions = sp.GetRequiredService<CoreOptions>();
            var coreOptionsAccessor = Microsoft.Extensions.Options.Options.Create(coreOptions);
            return new InterfaceHttpClient(logger, client, coreOptionsAccessor);
        });

        // Register Data Protection for secret encryption (MUST be before ApiKeys)
        var dataProtectionBuilder = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("./keys"))
            .SetApplicationName("SIPS.Connect");

        // Encrypt Data Protection keys at rest
        // Option 1: Use X509 certificate (Recommended for production)
        var certPath = configuration["DataProtection:CertificatePath"];
        var certPassword = configuration["DataProtection:CertificatePassword"];

        if (!string.IsNullOrEmpty(certPath) && File.Exists(certPath))
        {
            try
            {
                var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    certPath,
                    certPassword
                );
                dataProtectionBuilder.ProtectKeysWithCertificate(cert);
                var logger = services.BuildServiceProvider().GetService<ILogger<Program>>();
                logger?.LogInformation("Data Protection keys encrypted with certificate: {CertPath}", certPath);
            }
            catch (Exception ex)
            {
                var logger = services.BuildServiceProvider().GetService<ILogger<Program>>();
                logger?.LogError(ex, "Failed to load Data Protection certificate from {CertPath}", certPath);
            }
        }
        // Option 2: Use DPAPI on Windows
        else if (OperatingSystem.IsWindows())
        {
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
            var logger = services.BuildServiceProvider().GetService<ILogger<Program>>();
            logger?.LogInformation("Data Protection keys encrypted with Windows DPAPI");
        }
        // Option 3: Warn if no encryption (Development only)
        else
        {
            var logger = services.BuildServiceProvider().GetService<ILogger<Program>>();
            logger?.LogWarning("⚠️  Data Protection keys are stored UNENCRYPTED. For production, configure DataProtection:CertificatePath in appsettings.json");
        }

        // Register Secret Management Services (MUST be before ApiKeys)
        services.AddSingleton<ISecretManagementService, SecretManagementService>();
        services.AddSingleton<SecretManagementTool>();

        // Register API Key Provider for dynamic reloading
        services.AddSingleton<IApiKeyProvider, ApiKeyProvider>();

        var corsPolicy = new CorsPolicies();
        configuration.Bind(nameof(CorsPolicies), corsPolicy);

        // Register ApiKeys using the provider (reads fresh from config each time)
        services.AddScoped<ApiKeys>(sp =>
        {
            var apiKeyProvider = sp.GetRequiredService<IApiKeyProvider>();
            var apiKeys = apiKeyProvider.GetApiKeys();
            return new ApiKeys(apiKeys);
        });

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

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "MultiAuth";
            options.DefaultChallengeScheme = "MultiAuth";
        })
        .AddPolicyScheme("MultiAuth", "JWT or API Key", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var auth = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return JwtBearerDefaults.AuthenticationScheme;

                if (context.Request.Headers.ContainsKey(ApiKeyDefaults.HeaderNameKey))
                    return ApiKeyDefaults.AuthenticationScheme;

                // fallback to JWT so Unauthorized is returned if neither header is present
                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
       .AddJwtBearer(options =>
       {
           var host = configuration["Keycloak:Realm:Host"];
           var protocol = configuration["Keycloak:Realm:Protocol"];
           var realm = configuration["Keycloak:Realm:Name"];
           var audience = configuration["Keycloak:Realm:Audience"] ?? throw new ArgumentNullException("Keycloak:Realm:Audience is required in appSettings.json");
           var validateIssuer = bool.Parse(configuration["Keycloak:Realm:ValidateIssuer"] ?? "true");
           string[] validIssuers = configuration.GetSection("Keycloak:Realm:ValidIssuers").Get<string[]>() ?? Array.Empty<string>();

           var authority = $"{protocol}://{host}/realms/{realm}";

           options.Authority = authority;
           options.Audience = audience;
           options.RequireHttpsMetadata = false;
           options.MapInboundClaims = false;
           options.RefreshOnIssuerKeyNotFound = true;

           options.TokenValidationParameters.ValidateIssuer = validateIssuer;
           options.TokenValidationParameters.ValidIssuer              = authority;
            options.TokenValidationParameters.ValidateAudience         = true;
            options.TokenValidationParameters.ValidAudience            = audience;
            options.TokenValidationParameters.ValidateLifetime         = true;
            options.TokenValidationParameters.RoleClaimType            = ClaimTypes.Role;
            options.TokenValidationParameters.ValidIssuers             = validIssuers;

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
       })
       .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyDefaults.AuthenticationScheme, _ => { });

        // Register Core Authentication Service
        services.AddSingleton<ICoreAuthService, CoreAuthService>();

        // Register Health Check Service
        services.AddScoped<IHealthCheckService, HealthCheckService>();

        // Register Live Participants Service
        services.AddScoped<ILiveParticipantsService, LiveParticipantsService>();

        // Register Balance Monitoring Service
        services.AddScoped<IBalanceMonitoringService, BalanceMonitoringService>();

        // Shared schema-backed WP-SIPS information-service client.
        services.AddScoped<SipsInformationProtocolClient>();
        services.AddSingleton<IParticipantOperationRouter, ParticipantOperationRouter>();
        services.AddSingleton<IPapssHealthState, PapssHealthState>();
        services.AddScoped<IPapssCallbackGuard, PapssCallbackGuard>();
        services.AddSingleton<IParticipantCallbackContext, ParticipantCallbackContext>();
        services.AddHttpClient<IPapssFacingSipsClient, PapssFacingSipsClient>();

        services.AddSingleton<ILogService, LogService>();
        
        services.AddCore(configuration);
        services.RemoveAll<IJsonAdapter>();
        services.AddSingleton<JsonAdapter>();
        services.AddSingleton<IJsonAdapter, ParticipantCallbackJsonAdapter>();
        services.RemoveAll<ICallbackClient>();
        services.AddSingleton<CallbackClient>();
        services.AddSingleton<ICallbackClient, ParticipantCallbackClient>();
    }

    private static void ValidatePapssFacing(PapssFacingOptions options, IConfiguration configuration)
    {
        if (!options.Enabled) return;
        if (!Uri.TryCreate(options.IsoIngressUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl must be an absolute HTTP(S) URI when PAPSS is enabled.");
        if (!string.Equals(uri.AbsolutePath.TrimEnd('/'), "/sips/messages", StringComparison.Ordinal))
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl must target the common /sips/messages ingress.");
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl must use HTTPS except for loopback test environments.");
        if (options.AllowedHosts.Length == 0 || !options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("PapssFacing:IsoIngressUrl host must be explicitly allowed.");
        if (string.IsNullOrWhiteSpace(options.Environment) || string.IsNullOrWhiteSpace(options.RemoteWpSipsIdentity) || string.IsNullOrWhiteSpace(options.SecurityProfile))
            throw new InvalidOperationException("PapssFacing responder identity, environment and security profile are required when PAPSS is enabled.");
        if (options.RequestTimeoutSeconds is < 1 or > 120 || options.MaximumResponseBytes is < 1024 or > 10_000_000)
            throw new InvalidOperationException("PapssFacing timeout or response-size limit is invalid.");
        if (options.AllowedLocalInstruments.Length == 0)
            throw new InvalidOperationException("PapssFacing requires an explicit allowed-local-instrument list when enabled.");
        if (options.AllowedCorridors.Length == 0 || options.ReadinessStaleSeconds is < 10 or > 86400)
            throw new InvalidOperationException("PapssFacing requires explicit corridors and a valid readiness staleness interval when enabled.");
        foreach (var (principal, participant) in options.Participants)
        {
            if (!participant.Enabled) continue;
            if (string.IsNullOrWhiteSpace(principal) || string.IsNullOrWhiteSpace(participant.Bic))
                throw new InvalidOperationException("Each enabled PAPSS participant requires a configuration key and BIC.");
            if (options.Participants.Where(x => x.Value.Enabled).Count(x => string.Equals(x.Value.Bic, participant.Bic, StringComparison.OrdinalIgnoreCase)) != 1)
                throw new InvalidOperationException("Each enabled PAPSS participant BIC must be unique.");
            if (string.IsNullOrWhiteSpace(participant.CallbackMappingProfile) || !configuration.GetSection("Endpoints").GetChildren().Any(x => x.Key.StartsWith(participant.CallbackMappingProfile + ".", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Enabled PAPSS participant '{principal}' requires a configured callback mapping profile.");
            if (!Uri.TryCreate(participant.CallbackUrl, UriKind.Absolute, out var callbackUri) || callbackUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Enabled PAPSS participant '{principal}' requires an HTTPS callback URL.");
        }
    }
}
