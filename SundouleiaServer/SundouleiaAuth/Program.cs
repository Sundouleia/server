namespace SundouleiaAuth;

public class Program
{
    public static void Main(string[] args)
    {
        var hostBuilder = CreateHostBuilder(args);
        using var host = hostBuilder.Build();
        try
        {
            host.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    /// <summary>
    ///     Create the HostBuilder for the Sundouleia Authentication Server
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.ClearProviders();
            builder.AddConsole();
        });
        // Use the factory to make the logger for this host.
        var logger = loggerFactory.CreateLogger<Startup>();

        // Then return the constructed host builder with some arguments -->
        return Host.CreateDefaultBuilder(args)
            .UseSystemd()
            .UseConsoleLifetime()
            .ConfigureAppConfiguration((ctx, config) =>
            {
                var appSettingsPath = Environment.GetEnvironmentVariable("APPSETTINGS_PATH");
                if (!string.IsNullOrEmpty(appSettingsPath))
                    config.AddJsonFile(appSettingsPath, optional: true, reloadOnChange: true);
                else
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                // add other environmental variables defined within the service.
                config.AddEnvironmentVariables();
            })
            // configure the web host defaults to...
            .ConfigureWebHostDefaults(webBuilder =>
            {
                // use the content root as the base directory
                webBuilder.UseContentRoot(AppContext.BaseDirectory);
                webBuilder.ConfigureLogging((ctx, builder) =>
                {
                    builder.AddConfiguration(ctx.Configuration.GetSection("Logging"));
                    builder.AddFile(o => o.RootPath = AppContext.BaseDirectory);
                });
                // and to use the startup as the startup class for the web host
                webBuilder.UseStartup(ctx => new Startup(ctx.Configuration, logger));
            });
    }
}
