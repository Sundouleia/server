using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Hub;
using SundouleiaServer.Hubs;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Services;
using SundouleiaShared.Utils;
using SundouleiaShared.Utils.Configuration;

namespace SundouleiaServer.Services;
/// <summary> Service for cleaning up users and groups that are no longer active </summary>
public class UserCleanupService : BackgroundService
{
    private readonly SundouleiaMetrics _metrics;
    private readonly IConfigurationService<ServerConfig> _config;
    private readonly IDbContextFactory<SundouleiaDbContext> _dbContextFactory;
    private readonly ILogger<UserCleanupService> _logger;
    private readonly IHubContext<SundouleiaHub, ISundouleiaHub> _hubContext;

    public UserCleanupService(SundouleiaMetrics metrics, IConfigurationService<ServerConfig> config,
        IDbContextFactory<SundouleiaDbContext> dbContextFactory, ILogger<UserCleanupService> logger, 
        IHubContext<SundouleiaHub, ISundouleiaHub> hubContext)
    {
        _metrics = metrics;
        _config = config;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _hubContext = hubContext;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleanup Service started");
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (SundouleiaDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                // Remove accounts that have been inactive longer than the configured day count.
                await PurgeUnusedAccounts(dbContext).ConfigureAwait(false);
                // If an account's reputation had a timeout that expired, reset it.
                await ResetReputationTimeouts(dbContext).ConfigureAwait(false);

                CleanUpTimedOutAccountAuthClaims(dbContext);
                CleanupOutdatedPairRequests(dbContext);

                dbContext.SaveChanges();
            }
            DateTime now = DateTime.Now;
            TimeOnly currentTime = new(now.Hour, now.Minute, now.Second);
            TimeOnly futureTime = new(now.Hour, now.Minute - now.Minute % 10, 0);
            TimeSpan span = futureTime.AddMinutes(10) - currentTime;

            _logger.LogInformation("User Cleanup Complete, next run at {date}", now.Add(span));
            await Task.Delay(span, ct).ConfigureAwait(false);
        }
    }

    private async Task ResetReputationTimeouts(SundouleiaDbContext dbContext)
    {
        try
        {
            _logger.LogInformation("Resetting Reputation Timeouts");

            var curTime = DateTime.UtcNow;
            var reputationsToFix = await dbContext.AccountReputation.Where(rep => rep.NeedsTimeoutReset).ToListAsync().ConfigureAwait(false);
            foreach (var rep in reputationsToFix)
            {
                if (rep.ProfileViewTimeout != DateTime.MinValue && rep.ProfileViewTimeout < curTime)
                {
                    rep.ProfileViewing = true;
                    rep.ProfileViewTimeout = DateTime.MinValue;
                }
                if (rep.ProfileEditTimeout != DateTime.MinValue && rep.ProfileEditTimeout < curTime)
                {
                    rep.ProfileEditing = true;
                    rep.ProfileEditTimeout = DateTime.MinValue;
                }
                if (rep.RadarTimeout != DateTime.MinValue && rep.RadarTimeout < curTime)
                {
                    rep.RadarUsage = true;
                    rep.RadarTimeout = DateTime.MinValue;
                }
                if (rep.ChatTimeout != DateTime.MinValue && rep.ChatTimeout < curTime)
                {
                    rep.ChatUsage = true;
                    rep.ChatTimeout = DateTime.MinValue;
                }
                _logger.LogDebug($"TimeoutCleanup: Reset Timeouts for [{rep.UserUID}]");
            }
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Timeout Cleanup.");
        }
    }
    private async Task PurgeUnusedAccounts(SundouleiaDbContext dbContext)
    {
        try
        {
            if (_config.GetValueOrDefault(nameof(ServerConfig.PurgeUnusedAccounts), false))
            {
                int usersOlderThanDays = _config.GetValueOrDefault(nameof(ServerConfig.PurgeUnusedAccountsPeriodInDays), 300);

                _logger.LogInformation("Cleaning up users older than {usersOlderThanDays} days", usersOlderThanDays);

                IQueryable<User> allUsers = dbContext.Users.AsNoTracking().Where(u => string.IsNullOrEmpty(u.Alias));
                List<User> usersToRemove = new List<User>();
                foreach (User user in allUsers)
                {
                    if (user.LastLogin < DateTime.UtcNow - TimeSpan.FromDays(usersOlderThanDays))
                    {
                        _logger.LogInformation("User outdated: {userUID}", user.UID);
                        usersToRemove.Add(user);
                    }
                }

                foreach (User user in usersToRemove)
                {
                    Dictionary<string, List<string>> remUserPairUidDict = await SharedDbFunctions.DeleteUserProfile(user, _logger, dbContext, _metrics).ConfigureAwait(false);
                    // inform all related pairs to remove the user.
                    foreach ((string removedUser, List<string> removedPairUids) in remUserPairUidDict)
                        await _hubContext.Clients.Users(removedPairUids).Callback_RemovePair(new(new(removedUser))).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Cleaning up unauthorized users");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during user purge");
        }
    }

    private void CleanupOutdatedPairRequests(SundouleiaDbContext dbContext)
    {
        try
        {
            _logger.LogInformation("Cleaning up expired pair requests");
            var expiredRequests = dbContext.Requests.Where(r => r.CreationTime < DateTime.UtcNow - TimeSpan.FromDays(3)).ToList();
            dbContext.Requests.RemoveRange(expiredRequests);
            _logger.LogInformation($"Removing [{expiredRequests.Count}] expired requests");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during pair request cleanup");
        }
    }

    private void CleanUpTimedOutAccountAuthClaims(SundouleiaDbContext dbContext)
    {
        try
        {
            _logger.LogInformation($"Cleaning up expired account claim authentications");
            var activeClaimEntries = dbContext.AccountClaimAuth.Include(u => u.User).Where(a => a.StartedAt != null).ToList();

            var entriesToRemove = activeClaimEntries.Where(a => a.StartedAt != null && a.StartedAt < DateTime.UtcNow - TimeSpan.FromMinutes(15));
            // We dont want to remove the users themselves, because it would be unfair if someone spent 2 months unverified, tried to
            // verify, it failed, timed out, then they got their profile deleted.

            // Instead, just remove the unclaimed auth entries with expired times.
            // These users should be removed as their authentications have expired.
            dbContext.RemoveRange(entriesToRemove);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during expired auths cleanup");
        }
    }
}
