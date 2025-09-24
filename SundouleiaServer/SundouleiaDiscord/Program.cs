using SundouleiaShared.Data;
using SundouleiaShared.Services;
using SundouleiaShared.Utils.Configuration;

namespace SundouleiaDiscord;
// the main program class for the sundouleia discord bot / discord center.
public class Program
{
    public static void Main(string[] args)
    {
        var hostBuilder = CreateHostBuilder(args);
        var host = hostBuilder.Build();

        // Before we run the host, we want to make sure that we log warnings
        // for any potential issues with the service provider or db context.
        using (var scope = host.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            using var dbContext = services.GetRequiredService<SundouleiaDbContext>();
            // Get the Discord configuration options.
            var options = host.Services.GetService<IConfigurationService<DiscordConfig>>();
            var optionsServer = host.Services.GetService<IConfigurationService<ServerConfig>>();
            var logger = host.Services.GetService<ILogger<Program>>();

            // Both should be valid here!
            if (optionsServer is null) logger.LogWarning("ServerConfig options are null.");
            if (options is null) logger.LogWarning("DiscordConfig options are null.");
        }

        host.Run();
    }

    /// <summary>
    ///     Create the HostBuilder for the Sundouleia Discord Server
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args)
        => Host.CreateDefaultBuilder(args)
        .UseSystemd()
        .UseConsoleLifetime()
        .ConfigureWebHostDefaults(webBuilder =>
        {
            // Use the base directory as the content root.
            webBuilder.UseContentRoot(AppContext.BaseDirectory);
            webBuilder.ConfigureLogging((ctx, builder) =>
            {
                // Add the logging configuration.
                builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                // Add the file logger.
                builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
            });
            // Use the Startup class as the startup for the web host.
            webBuilder.UseStartup<Startup>();
        });
}
