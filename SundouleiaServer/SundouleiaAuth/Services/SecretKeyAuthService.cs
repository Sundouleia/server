using Microsoft.EntityFrameworkCore;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Services;
using SundouleiaShared.Utils.Configuration;
using System.Collections.Concurrent;

namespace SundouleiaAuth.Services;

/// <summary> Authenticator Service for our secret key authentication system. </summary>
public class SecretKeyAuthService
{
    private readonly ILogger<SecretKeyAuthService> _logger;
    private readonly IDbContextFactory<SundouleiaDbContext> _dbContextFactory;
    private readonly IConfigurationService<AuthServiceConfig> _configurationService;
    private readonly SundouleiaMetrics _metrics;

    private readonly ConcurrentDictionary<string, SecretKeyFailedAuthorization> _failedAuthorizations = new(StringComparer.Ordinal);

    public SecretKeyAuthService(
        ILogger<SecretKeyAuthService> logger,
        IDbContextFactory<SundouleiaDbContext> dbContextFactory,
        IConfigurationService<AuthServiceConfig> configuration,
        SundouleiaMetrics metrics)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _configurationService = configuration;
        _metrics = metrics;
    }

    /// <summary>
    ///     Authorize a user with by a passed in hashed secret key, and the IP it is accessed from.
    /// </summary>
    /// <returns> The secret key authorization reply. </returns>
    public async Task<SecretKeyAuthReply> AuthorizeAsync(string ip, string hashedSecretKey)
    {
        // increase the counter of our metrics for the authentication requests.
        _metrics.IncCounter(MetricsAPI.CounterAuthRequests);

        // If the IP authorizing is in the failed list, and attempts > failed for temp ban, then temp ban the IP
        if (_failedAuthorizations.TryGetValue(ip, out var failedIpAuths)
        && failedIpAuths.FailedAttempts > _configurationService.GetValueOrDefault(nameof(AuthServiceConfig.FailedAuthForTempBan), 5))
        {
            // If reset task is null, do the temp ban thing.
            if (failedIpAuths.ResetTask is null)
            {
                _logger.LogWarning("TempBan {ip} for authorization spam", ip);
                failedIpAuths.ResetTask = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(_configurationService.GetValueOrDefault(nameof(AuthServiceConfig.TempBanDurationInMinutes), 5))).ConfigureAwait(false);
                }).ContinueWith((t) => // then when the task is done, remove the IP from the failed authorizations list.
                {
                    _failedAuthorizations.Remove(ip, out _);
                });
            }
            // Otherwise just return the temp ban reply.
            return new(Success: false, Uid: null!, AccountUid: null!, Alias: null!, TempBan: true, Permaban: false);
        }

        // Otherwise grab the dbContext to get our auth.
        using var context = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);

        if (await context.Auth.Include(a => a.User).Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(a => a.HashedKey == hashedSecretKey).ConfigureAwait(false) is not { } authReply)
            return AuthenticationFailure(ip);

        // Finalize the reply.
        SecretKeyAuthReply reply = new(true, authReply.UserUID, authReply.PrimaryUserUID, authReply.User.Alias, false, authReply.AccountRep.IsBanned);
        _metrics.IncCounter(MetricsAPI.CounterAuthSuccess);
        return reply;
    }

    private SecretKeyAuthReply AuthenticationFailure(string ip)
    {
        _metrics.IncCounter(MetricsAPI.CounterAuthFailed);

        // Dont even think we use this anymore so it shouldnt really madder, but in case we need it later, keep it.
        _logger.LogWarning($"Failed authorization from {ip}");
        var whitelisted = _configurationService.GetValueOrDefault(nameof(AuthServiceConfig.WhitelistedIps), new List<string>());

        // if the IP does not exist in the list of whitelisted IPs, then increase the failed attempts for the IP.
        if (!whitelisted.Exists(w => ip.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            // if the IP is in the failed authorizations list, then increase the failed attempts for the IP.
            if (_failedAuthorizations.TryGetValue(ip, out var auth))
                auth.IncreaseFailedAttempts();
            else
                // otherwise, add the IP to the failed authorizations list.
                _failedAuthorizations[ip] = new SecretKeyFailedAuthorization();
        }

        // return the failed SecretKeyAuthReply object.
        return new(Success: false, Uid: null!, AccountUid: null!, Alias: null!, TempBan: false, Permaban: false);
    }
}
#pragma warning restore CS8604