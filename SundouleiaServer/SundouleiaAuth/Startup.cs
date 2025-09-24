using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;
using StackExchange.Redis.Extensions.Core.Configuration;
using StackExchange.Redis.Extensions.System.Text.Json;
using SundouleiaAuth.Controllers;
using SundouleiaAuth.Services;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.RequirementHandlers;
using SundouleiaShared.Services;
using SundouleiaShared.Utils;
using SundouleiaShared.Utils.Configuration;
using System.Net;
using System.Text;

namespace SundouleiaAuth;

public class Startup
{
    private readonly IConfiguration _config;
    private ILogger<Startup> _logger;

    public Startup(IConfiguration configuration, ILogger<Startup> logger)
    {
        _config = configuration;
        _logger = logger;
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        // get the configuration service from the config base class
        IConfigurationService<SundouleiaConfigBase> config = app.ApplicationServices.GetRequiredService<IConfigurationService<SundouleiaConfigBase>>();

        // This is done via HTTPS, we want routing.
        // Also make sure to log our metrics and use authentication and authorization.
        app.UseRouting();
        app.UseHttpMetrics();
        app.UseAuthentication();
        app.UseAuthorization();

        // CLEANUP: Dont think this is needed, but could be wrong...
        KestrelMetricServer metricServer = new KestrelMetricServer(6152);
        metricServer.Start();

        // Define endpoints for the mapped controllers.
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            // but also that for each source in the datasources of our endpoints, we log the url of the endpoint.
            foreach (RouteEndpoint? source in endpoints.DataSources.SelectMany(e => e.Endpoints).Cast<RouteEndpoint>())
            {
                if (source is null) continue;
                _logger.LogInformation($"Endpoint: {source.RoutePattern.RawText}");
            }
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Get the required ConfigurationSection from the appsettings.json file under the subsection "Sundouleia"
        var sundouleiaConfig = _config.GetRequiredSection("Sundouleia");

        // Add HTTP Context first and foremost.
        services.AddHttpContextAccessor();

        // Add the redi's config and services next.
        ConfigureRedis(services, sundouleiaConfig);

        // Add our internal services.
        services.AddSingleton<SecretKeyAuthService>();
        // Configure internal services for the configsection.
        services.Configure<AuthServiceConfig>(sundouleiaConfig);
        services.Configure<SundouleiaConfigBase>(sundouleiaConfig);

        // Append the token generator.
        services.AddSingleton<ServerTokenGenerator>();

        // Not configure the remaining services in order, like they are in the other server startups.
        ConfigureAuthorization(services);

        ConfigureDatabase(services, sundouleiaConfig);

        services.AddSingleton<IConfigurationService<AuthServiceConfig>, SundouleiaConfigServiceServer<AuthServiceConfig>>();
        services.AddSingleton<IConfigurationService<SundouleiaConfigBase>, SundouleiaConfigServiceServer<SundouleiaConfigBase>>();

        ConfigureMetrics(services);

        // Finally, add our Sundouleia background service and the controller for our application feature providers.
        services.AddHealthChecks();

        // **This lets us scope access to auth component of the sundouleia server.
        services.AddControllers().ConfigureApplicationPartManager(a =>
        {
            // remove the default feature provider
            a.FeatureProviders.Remove(a.FeatureProviders.OfType<ControllerFeatureProvider>().First());
            // then append our own with a limited scope of our JwtController.
            a.FeatureProviders.Add(new AllowedControllersFeatureProvider(typeof(JwtController)));
        });
    }

    /// <summary>
    ///     Helper method to configure the authorization for the sundouleia authentication server.
    /// </summary>
    private static void ConfigureAuthorization(IServiceCollection services)
    {
        // add the user and valid token requirement handlers.
        services.AddTransient<IAuthorizationHandler, UserRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ValidTokenRequirementHandler>();

        // add the jwt bearer options to the services
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            // configure the authentication scheme format
            .Configure<IConfigurationService<SundouleiaConfigBase>>((options, config) =>
            {
                // to have the options for valid parameters set up
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = false, // we do not need to validate the issuer
                    ValidateLifetime = true, // but we do need to validate the lifetime of the token
                    ValidateAudience = false, // we do not need to validate the audience
                    ValidateIssuerSigningKey = true, // but we do need to validate the issuer signing key
                    // and we need to set the issuerSigningKey to the jwt key from the config
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.ASCII.GetBytes(config.GetValue<string>(nameof(SundouleiaConfigBase.Jwt)))),
                };
            });

        // add the authentication to the services with the default bearer scheme
        services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        // add the authorization options to the services
        services.AddAuthorization(options =>
        {
            // set the default policy to require an authenticated user
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build();

            // next, lets add our policies for different states people can have.
            // New TemporaryAccess policy
            options.AddPolicy("TemporaryAccess", policy =>
            {
                policy.AddRequirements(new UserRequirement(UserRequirements.TempAccess));
            });

            // to be authenticated, you must satisfy the policy criteria stating that
            options.AddPolicy("Authenticated", policy =>
            {
                // you must have a valid token
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                // and you must be an authenticated user
                policy.RequireAuthenticatedUser();
                // and you must satisfy the valid token requirement
                policy.AddRequirements(new ValidTokenRequirement());
            });
            // to be identified, you must
            options.AddPolicy("Identified", policy =>
            {
                // satisfy the user requirement
                policy.AddRequirements(new UserRequirement(UserRequirements.Identified));
                // and satisfy the valid token requirement
                policy.AddRequirements(new ValidTokenRequirement());

            });
            // to be an admin, you must
            options.AddPolicy("Admin", policy =>
            {
                // satisfy the user requirement for identified and administrator
                policy.AddRequirements(new UserRequirement(UserRequirements.Identified | UserRequirements.Admin));
                // and satisfy the valid token requirement
                policy.AddRequirements(new ValidTokenRequirement());

            });
            // to be internal, you must satisfy the internal claim requirement for the token to be true (internal)
            options.AddPolicy("Internal", new AuthorizationPolicyBuilder().RequireClaim(SundouleiaClaimTypes.Internal, "true").Build());
        });
    }

    /// <summary> Helper method to configure the Sundouleia Metrics for the sundouleia authentication server. </summary>
    private static void ConfigureMetrics(IServiceCollection services)
    {
        // add to the service collection the sundouleia metrics for the authentication server
        services.AddSingleton<SundouleiaMetrics>(m => new SundouleiaMetrics(m.GetService<ILogger<SundouleiaMetrics>>(), new List<string>
        {
            MetricsAPI.CounterAuthRequests,
            MetricsAPI.CounterAuthSuccess,
            MetricsAPI.CounterAuthFailed,
            MetricsAPI.CounterDeletedVerifiedUsers,
        }, new List<string>
        {
            MetricsAPI.GaugeAuthorizedConnections,
            MetricsAPI.GaugeConnections,
        }));
    }

    /// <summary>
    ///     Helper method to configure the redi's for the authentication server component of the sundouleia server.
    /// </summary>
    private static void ConfigureRedis(IServiceCollection services, IConfigurationSection sundouleiaConfig)
    {
        // define the redi's connection string as the string provided in the serverConfig
        string redisConnection = sundouleiaConfig.GetValue(nameof(ServerConfig.RedisConnectionString), string.Empty);
        // fetch the options from parsing out the redi's connection
        ConfigurationOptions options = ConfigurationOptions.Parse(redisConnection);

        // get the endpoint from the options
        EndPoint endpoint = options.EndPoints[0];
        // set the address to an empty string for now and the port to 0.
        string address = "";
        int port = 0;
        // check if the endpoint is dnsEndpoint. If it is, set address to the dnsEndpoint.Host, and port to the dnsEndpoint.Port.
        if (endpoint is DnsEndPoint dnsEndPoint) 
        {
            address = dnsEndPoint.Host; 
            port = dnsEndPoint.Port;
        }
        // otherwise, if it is an IP endpoint, then set the address to the ipEndpoint.Address.ToString(), and the port to the ipEndpoint.Port.
        if (endpoint is IPEndPoint ipEndPoint)
        {
            address = ipEndPoint.Address.ToString(); 
            port = ipEndPoint.Port;
        }

        // set up the redi's configuration for the server
        var redisConfiguration = new RedisConfiguration()
        {
            AbortOnConnectFail = false,  // abort redi's connection on failure.
            KeyPrefix = "", // clear the key prefix to be blank.
            Hosts = new RedisHost[] // set the new Redi's host to the address and port.
            {
                new RedisHost(){ Host = address, Port = port },
            },
            AllowAdmin = true, // allow the admin to have access to the redi's server.
            ConnectTimeout = options.ConnectTimeout, // set the connection timeout duration to the timeout from the options.
            Database = 0,   // set the database to 0. (none)
            Ssl = false,    // do not require ssl
            Password = options.Password, // set the password to the password from the options.
            ServerEnumerationStrategy = new ServerEnumerationStrategy() // define the server enumeration strategy
            {
                Mode = ServerEnumerationStrategy.ModeOptions.All, // set the mode to all
                TargetRole = ServerEnumerationStrategy.TargetRoleOptions.Any, // set the target role to any
                UnreachableServerAction = ServerEnumerationStrategy.UnreachableServerActionOptions.Throw, // throw an exception if the server is unreachable
            },
            MaxValueLength = 1024, // max val length (huh?)
            PoolSize = sundouleiaConfig.GetValue(nameof(ServerConfig.RedisPool), 50), // set the number of connections in the pool to the value in the config, or 50.
            SyncTimeout = options.SyncTimeout, // determine the sync timeout for redi's.
            LoggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>(), // get the logger factory from the services.
        };

        services.AddStackExchangeRedisExtensions<SystemTextJsonSerializer>(redisConfiguration);
    }

    /// <summary>
    ///     Database context configuration helper.
    /// </summary>
    private void ConfigureDatabase(IServiceCollection services, IConfigurationSection sundouleiaConfig)
    {
        // add the sundouleia database context to our services, set up with customized options.
        services.AddDbContextPool<SundouleiaDbContext>(options =>
        {
            // use Postgresql as the database provider, with the connection string from the appsettings.json file.
            options.UseNpgsql(_config.GetConnectionString("DefaultConnection"), builder =>
            {
                // set the builders migrations history table to ef migrations history in the public schema.
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
                // and migrate the assembly from SundouleiaShared (where the SundouleiaDbContext is located)
                builder.MigrationsAssembly("SundouleiaShared");
                // use the snakeCaseNamingConvention
            }).UseSnakeCaseNamingConvention();
            // do not include thread safety checks
            options.EnableThreadSafetyChecks(false);
            // and set the pool size to the value in the configBase, or 1024
        }, sundouleiaConfig.GetValue(nameof(SundouleiaConfigBase.DbContextPoolSize), 1024));

        // finally, add the sundouleia dbcontext FACTORY (different from pool)
        services.AddDbContextFactory<SundouleiaDbContext>(options =>
        {
            // use the same connectionstring as above)
            options.UseNpgsql(_config.GetConnectionString("DefaultConnection"), builder =>
            {
                // (with the same options as above)
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
                builder.MigrationsAssembly("SundouleiaShared");
            }).UseSnakeCaseNamingConvention();
            options.EnableThreadSafetyChecks(false);
        });
    }
}
