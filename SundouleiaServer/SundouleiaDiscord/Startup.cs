using MessagePack;
using MessagePack.Resolvers;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Services;
using SundouleiaShared.Utils;
using SundouleiaShared.Utils.Configuration;

namespace SundouleiaDiscord;

public class Startup
{
    private readonly IConfiguration _config;

    public Startup(IConfiguration config)
    {
        _config = config;
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // first, get the config service
        var config = app.ApplicationServices.GetRequiredService<IConfigurationService<SundouleiaConfigBase>>();

        // TODO: Do Metrics right someday, but that day is not today.
        // next, set up the metrics server at the port specified in the config located in the appsettings.json file. Otherwise, use port 4982.
/*        using var metricServer = new KestrelMetricServer(config.GetValueOrDefault<int>(nameof(SundouleiaConfigBase.MetricsPort), 4982));
        metricServer.Start();*/

        // Discord uses basic routing.
        app.UseRouting();
        // A workaround hack for redi's (thanks mare again for figuring this out).
        app.UseEndpoints(e => e.MapHub<SundouleiaServer.Hubs.SundouleiaHub>("/dummyhub"));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Get the required ConfigurationSection from the appsettings.json file under the subsection "Sundouleia"
        var sundouleiaConfig = _config.GetSection("Sundouleia");

        // Configure out DbContext pool.
        services.AddDbContextPool<SundouleiaDbContext>(options =>
        {
            options.UseNpgsql(_config.GetConnectionString("DefaultConnection"), builder =>
            {
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
            }).UseSnakeCaseNamingConvention();
            options.EnableThreadSafetyChecks(false); // do not include thread safety checks
        }, _config.GetValue(nameof(SundouleiaConfigBase.DbContextPoolSize), 1024));
        // And the factory to generate said context.
        services.AddDbContextFactory<SundouleiaDbContext>(options =>
        {
            options.UseNpgsql(_config.GetConnectionString("DefaultConnection"), builder =>
            {
                builder.MigrationsHistoryTable("_efmigrationshistory", "public");
                builder.MigrationsAssembly("SundouleiaShared");
            }).UseSnakeCaseNamingConvention();
            options.EnableThreadSafetyChecks(false);
        });

        // Append Metrics
        services.AddSingleton(m => new SundouleiaMetrics(m.GetService<ILogger<SundouleiaMetrics>>(), new List<string> { }, new List<string> { }));

        // Setup Redis.
        var redis = sundouleiaConfig.GetValue(nameof(ServerConfig.RedisConnectionString), string.Empty);
        var options = ConfigurationOptions.Parse(redis);
        options.ClientName = "Sundouleia";
        options.ChannelPrefix = new RedisChannel("UserData", RedisChannel.PatternMode.Literal);
        // configure multiplexer for the redi's connection
        ConnectionMultiplexer connectionMultiplexer = ConnectionMultiplexer.Connect(options);
        // Inject the configured Redis connection multiplexer.
        services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);

        // Build the signalR for the dummyhub so we can use discord commands to interface with Sundouleia servers.
        var signalRServiceBuilder = services.AddSignalR(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = long.MaxValue;
            hubOptions.EnableDetailedErrors = true;
            hubOptions.MaximumParallelInvocationsPerClient = 10;
            hubOptions.StreamBufferCapacity = 200;
        })
        .AddMessagePackProtocol(opt =>
        {
            var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                BuiltinResolver.Instance,
                AttributeFormatterResolver.Instance,
                // replace enum resolver
                DynamicEnumAsStringResolver.Instance,
                DynamicGenericResolver.Instance,
                DynamicUnionResolver.Instance,
                DynamicObjectResolver.Instance,
                PrimitiveObjectResolver.Instance,
                // final fallback(last priority)
                StandardResolver.Instance);

            // and set the options serializer options to standard with lz4block compression and the resolver we just made
            opt.SerializerOptions = MessagePackSerializerOptions.Standard
                .WithCompression(MessagePackCompression.Lz4Block)
                .WithResolver(resolver);
        });

        // now we can pull the string of our redi's ConnectionString to get the connection string.
        var redisConnection = sundouleiaConfig.GetValue(nameof(SundouleiaConfigBase.RedisConnectionString), string.Empty);
        // and add the redi's connection to the signalR service builder with the options we made above.
        // (because we use signalR, this is why we have to use the hack for the dummyhub)
        signalRServiceBuilder.AddStackExchangeRedis(redisConnection, options => { });

        // Add the configs from discord and the server and the base.
        services.Configure<DiscordConfig>(sundouleiaConfig);
        services.Configure<ServerConfig>(sundouleiaConfig);
        services.Configure<SundouleiaConfigBase>(sundouleiaConfig);

        // Inject the remaining services.
        services.AddSingleton(_config);
        services.AddSingleton<ServerTokenGenerator>();
        services.AddSingleton<DiscordBotServices>();
        services.AddHostedService<DiscordBot>();
        services.AddSingleton<IConfigurationService<DiscordConfig>, SundouleiaConfigServiceServer<DiscordConfig>>();
        services.AddSingleton<IConfigurationService<ServerConfig>, SundouleiaConfigServiceClient<ServerConfig>>();
        services.AddSingleton<IConfigurationService<SundouleiaConfigBase>, SundouleiaConfigServiceClient<SundouleiaConfigBase>>();

        services.AddHostedService(p => (SundouleiaConfigServiceClient<SundouleiaConfigBase>)p.GetService<IConfigurationService<SundouleiaConfigBase>>());
        services.AddHostedService(p => (SundouleiaConfigServiceClient<ServerConfig>)p.GetService<IConfigurationService<ServerConfig>>());
    }
}