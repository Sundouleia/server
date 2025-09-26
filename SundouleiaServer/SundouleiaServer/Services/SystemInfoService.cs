using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Hubs;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Services;
using SundouleiaShared.Utils.Configuration;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace SundouleiaServer.Services;

public sealed class SystemInfoService : BackgroundService
{
    private readonly SundouleiaMetrics _metrics;
    private readonly IConfigurationService<ServerConfig> _config;
    private readonly IDbContextFactory<SundouleiaDbContext> _dbContextFactory;
    private readonly ILogger<SystemInfoService> _logger;
    private readonly IHubContext<SundouleiaHub, ISundouleiaHub> _hubContext;
    private readonly IRedisDatabase _redis;
    public ServerInfoResponse SystemInfoDto { get; private set; } = new(0);

    public SystemInfoService(SundouleiaMetrics metrics, IConfigurationService<ServerConfig> config, 
        IDbContextFactory<SundouleiaDbContext> dbContextFactory, ILogger<SystemInfoService> logger, 
        IHubContext<SundouleiaHub, ISundouleiaHub> hubContext, IRedisDatabase redis)
    {
        _metrics = metrics;
        _config = config;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _hubContext = hubContext;
        _redis = redis;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("System Info Service started");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var timeOut = _config.IsMain ? 15 : 60;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                ThreadPool.GetAvailableThreads(out int workerThreads, out int ioThreads);
                _metrics.SetGaugeTo(MetricsAPI.GaugeAvailableWorkerThreads, workerThreads);
                _metrics.SetGaugeTo(MetricsAPI.GaugeAvailableIOWorkerThreads, ioThreads);

                int onlineUsers = (_redis.SearchKeysAsync("SundouleiaHub:UID:*").GetAwaiter().GetResult()).Count();
                SystemInfoDto = new ServerInfoResponse(onlineUsers);
                if (_config.IsMain)
                {
                    // _logger.LogInformation($"Pushing system info: [{onlineUsers} users online]");
                    await _hubContext.Clients.All.Callback_ServerInfo(SystemInfoDto).ConfigureAwait(false);
                    using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

                    // lower how many things are being tracked by the db if it becomes too much.
                    _metrics.SetGaugeTo(MetricsAPI.GaugeAuthorizedConnections, onlineUsers);
                    _metrics.SetGaugeTo(MetricsAPI.GaugePairings, db.ClientPairs.AsNoTracking().Count());
                    _metrics.SetGaugeTo(MetricsAPI.GaugeUsersRegistered, db.Users.AsNoTracking().Count());
                }

                await Task.Delay(TimeSpan.FromSeconds(timeOut), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push system info");
            }
        }
    }
}