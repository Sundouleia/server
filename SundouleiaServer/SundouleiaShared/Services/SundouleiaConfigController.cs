using SundouleiaShared.Utils.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SundouleiaShared.Services;

// Controller to process values within the AppSettings.json files.
[Route("configuration/[controller]")]
[Authorize(Policy = "Internal")]
public class SundouleiaConfigController<T> : Controller where T : class, ISundouleiaConfiguration
{
    private readonly ILogger<SundouleiaConfigController<T>> _logger;
    private IOptionsMonitor<T> _config;

    public SundouleiaConfigController(IOptionsMonitor<T> config, ILogger<SundouleiaConfigController<T>> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet("GetConfigurationEntry")]
    [Authorize(Policy = "Internal")]
    public IActionResult GetConfigurationEntry(string key, string defaultValue)
    {
        var result = _config.CurrentValue.SerializeValue(key, defaultValue);
        // keep my sanity intact by logging the resulting interactions.
        _logger.LogInformation("Requested " + key + ", returning:" + result);
        return Ok(result);
    }
}

#pragma warning disable MA0048 // File name must match type name

/// <summary>
///     Config controller for the base configuration
/// </summary>
public class SundouleiaBaseConfigController : SundouleiaConfigController<SundouleiaConfigBase>
{
    public SundouleiaBaseConfigController(IOptionsMonitor<SundouleiaConfigBase> config, ILogger<SundouleiaBaseConfigController> logger) 
        : base(config, logger)
    { }
}

/// <summary>
///     Config Controller for the server.
/// </summary>
public class SundouleiaServerConfigController : SundouleiaConfigController<ServerConfig>
{
    public SundouleiaServerConfigController(IOptionsMonitor<ServerConfig> config, ILogger<SundouleiaServerConfigController> logger)
        : base(config, logger)
    { }
}

/// <summary> The controller for the discord configuration </summary>
public class SundouleiaDiscordConfigController : SundouleiaConfigController<DiscordConfig>
{
    public SundouleiaDiscordConfigController(IOptionsMonitor<DiscordConfig> config, ILogger<SundouleiaDiscordConfigController> logger) 
        : base(config, logger)
    { }
}
#pragma warning restore MA0048 // File name must match type name
