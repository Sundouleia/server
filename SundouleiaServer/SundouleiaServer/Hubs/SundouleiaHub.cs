using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis.Extensions.Core.Abstractions;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Services;
using SundouleiaServer.Utils;
using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Services;
using SundouleiaShared.Utils;
using SundouleiaShared.Utils.Configuration;
using System.Collections.Concurrent;

namespace SundouleiaServer.Hubs;
#pragma warning disable MA0011

public partial class SundouleiaHub : Hub<ISundouleiaHub>, ISundouleiaHub
{
    // Thread-Safe dictionary to track all users connected to the Sundouleia Hub.
    public static readonly ConcurrentDictionary<string, string> _userConnections = new(StringComparer.Ordinal);

    // Loggers and services.
    private readonly SundouleiaHubLogger _logger;
    private readonly SundouleiaMetrics _metrics;
    private readonly RadarService _radarService;
    private readonly SystemInfoService _systemInfoService;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IRedisDatabase _redis;

    // Lazily initialized DbContext generated on initialization.
    // Does not technically need to be lazy as it is not modified, but useful to have.
    private readonly Lazy<SundouleiaDbContext> _dbContextLazy;

    private SundouleiaDbContext DbContext => _dbContextLazy.Value;
    private readonly Version _expectedClientVersion;

    public SundouleiaHub(
        ILogger<SundouleiaHub> logger,
        IDbContextFactory<SundouleiaDbContext> dbFactory,
        IConfigurationService<ServerConfig> config,
        SundouleiaMetrics metrics,
        RadarService radarService,
        SystemInfoService systemInfoService,
        IRedisDatabase redis,
        IHttpContextAccessor contextAccessor)
    {
        _logger = new SundouleiaHubLogger(this, logger);
        _metrics = metrics;
        _radarService = radarService;
        _systemInfoService = systemInfoService;
        _contextAccessor = contextAccessor;
        _redis = redis;
        _dbContextLazy = new Lazy<SundouleiaDbContext>(() => dbFactory.CreateDbContext());

        _expectedClientVersion = config.GetValueOrDefault(nameof(ServerConfig.ExpectedClientVersion), new Version(0, 0, 0));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _dbContextLazy.IsValueCreated)
            DbContext.Dispose();

        base.Dispose(disposing);
    }

    /// <summary> 
    ///     Called whenever a client wishes to connect to the SundouleiaHub. <para />
    ///     If successful, a non-null connection response is returned with essential client information.
    /// </summary>
    /// <remarks>
    ///     Requires the requesting client to have authorization policy of "Identified" to proceed, 
    ///     meaning they used the one-time use account generation to create a user and key already.
    /// </remarks>
    [Authorize(Policy = "Identified")]
    public async Task<ConnectionResponse> GetConnectionResponse()
    {
        _logger.LogCallInfo();

        // Fail if not yet authenticated (identified)
        if (await DbContext.Auth.AsNoTracking().Include(a => a.User).FirstOrDefaultAsync(a => a.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, $"Secret key no longer exists in the DB. Inactive for too long.").ConfigureAwait(false);
            return null!;
        }
           
        // Inc the metrics for valid connections and inform the client that they are connected to the service.
        _metrics.IncCounter(MetricsAPI.CounterInitializedConnections);
        await Clients.Caller.Callback_ServerInfo(_systemInfoService.SystemInfoDto).ConfigureAwait(false);
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Welcome to Sundouleia! " +
            $"{_systemInfoService.SystemInfoDto.OnlineUsers} Users are online.\nI hope you have fun and enjoy~").ConfigureAwait(false);

        // All connected clients need a list of their account profile UIDs and their global permissions and account rep.

        // Determine if the auth is the primary account profile or alt profile. Gather the account list based on this.
        var accountProfiles = await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking()
            .Where(a => a.PrimaryUserUID == auth.PrimaryUserUID)
            .ToListAsync()
            .ConfigureAwait(false);

        // Try catch to handle cases where the SingleAsync fails. However, it should never fail, as all these created
        // upon a user addition.
        try
        {
            var globals = await DbContext.UserGlobalPerms.AsNoTracking().SingleAsync(g => g.UserUID == UserUID).ConfigureAwait(false);
            // Ret the connection response data.
            return new ConnectionResponse(auth.User.ToUserData())
            {
                CurrentClientVersion = _expectedClientVersion,
                ServerVersion = ISundouleiaHub.ApiVersion,

                GlobalPerms = globals.ToApi(),
                Reputation = accountProfiles[0].AccountRep.ToApi(),
                
                ActiveAccountUidList = accountProfiles.Select(ap => ap.UserUID).ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogCallWarning(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), "GetConnectionResponse", ex.Message, ex.StackTrace ?? string.Empty));
            return null!;
        }
    }

    /// <summary> 
    ///     Clients connecting to the server only to obtain a fresh User for their one-time account-generation.
    /// </summary>
    /// <returns> A tuple containing the UID and the hashed secret key for the one-time generation. </returns>
    [Authorize(Policy = "TemporaryAccess")]
    public async Task<(string, string)> OneTimeUseAccountGen()
    {
        // Need to create the User & Auth entries first before generating all associated content.
        // Ensure UID Uniqueness.
        var user = new User()
        {
            LastLogin = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        bool hasValidUid = false;
        while (!hasValidUid) // while its false, keep generating a new one.
        {
            string uid = StringUtils.GenerateRandomString(10);
            if (DbContext.Users.Any(u => u.UID == uid || u.Alias == uid)) continue;
            user.UID = uid;
            hasValidUid = true;
        }

        // Make an AccountRep entry for this user.
        var reputation = new AccountReputation()
        {
            UserUID = user.UID,
            User = user,
        };

        // Gen secret key 64long plus the current time.
        string computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString());
        
        // Add the auth for this user. In this case, the auth created is always the account's auth.
        // Add the respective Auth, referencing the User.
        var auth = new Auth()
        {
            HashedKey = StringUtils.Sha256String(computedHash),
            UserUID = user.UID,
            User = user,
            PrimaryUserUID = user.UID,
            PrimaryUser = user,
            AccountRep = reputation
        };

        // Initialize all attached data to the User entry across the database.
        await SharedDbFunctions.CreateMainProfile(user, reputation, auth, _logger.Logger, DbContext, _metrics).ConfigureAwait(false);

        _logger.LogMessage($"Created User [{user.UID} (Alias: {user.Alias})] Key -> {computedHash}");
        return (user.UID, computedHash);
    }


    /// <summary>
    ///     Effectively the "ping" method for connected clients. If they fail two in a row, they are disconnected.
    /// </summary>
    [Authorize(Policy = "Authenticated")]
    public async Task<bool> HealthCheck()
    {
        await UpdateUserOnRedis().ConfigureAwait(false);
        return false;
    }


    /// <summary> 
    ///     Called after fully connecting to Sundouleia servers. <para />
    ///     Both temporary and authenticated connections can be made with this call, so check context claims.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        /* ---- Process for temporary access connections. ---- */
        if (string.Equals(UserHasTempAccess, "LocalContent", StringComparison.Ordinal))
        {
            // allow the connection but dont store the user connection
            _logger.LogMessage("Temp Access Connection Established.");
            await base.OnConnectedAsync().ConfigureAwait(false);
            return;
        }

        /* ---- Regular Connection ---- */
        // It is possible that a user already connected could be connecting with a new connection ID. in this case we should update it.
        if (_userConnections.TryGetValue(UserUID, out string oldId))
        {
            _logger.LogCallInfo(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), "UpdatingId", oldId, Context.ConnectionId));
            _userConnections[UserUID] = Context.ConnectionId;
            // Leave their existing group chat if in any.
            await _radarService.LeaveRadarChat(oldId).ConfigureAwait(false);
        }
        // otherwise, attempt establishing a new connection.
        else
        {
            _metrics.IncGauge(MetricsAPI.GaugeConnections);
            // If this fails we should remove the user from the group and user connections.
            try
            {
                _logger.LogCallInfo(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), Context.ConnectionId, UserCharaIdent));
                // Place user in the redi's DB & add user's connectionID to the concurrent dictionary.
                await UpdateUserOnRedis().ConfigureAwait(false);
                _userConnections[UserUID] = Context.ConnectionId;
            }
            catch
            {
                // if at any point we catch an error, then remove the user from the concurrent dictionary of user connections.

                await _radarService.LeaveRadarChat(Context.ConnectionId).ConfigureAwait(false);
                // double check just to be safe.
                _userConnections.Remove(UserUID, out string removedId);
                await _radarService.LeaveRadarChat(removedId).ConfigureAwait(false);
            }
        }
        await base.OnConnectedAsync().ConfigureAwait(false);
    }


    /// <summary> 
    ///     Properly disposes of any connected or lingering data of a disconnected user.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception exception)
    {
        /* ---- Temporary Connection ---- */
        if (string.Equals(UserHasTempAccess, "LocalContent", StringComparison.Ordinal))
        {
            _logger.LogMessage("Temp Access Connection Disconnected.");
            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
            return;
        }

        /* -------------------- Regular Connection -------------------- */
        // Handle disconnect based on if you exist in the user connections or not.
        if (_userConnections.TryGetValue(UserUID, out string connectionId) && string.Equals(connectionId, Context.ConnectionId, StringComparison.Ordinal))
        {
            _metrics.DecGauge(MetricsAPI.GaugeConnections);
            try
            {
                _logger.LogCallInfo(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), Context.ConnectionId, UserCharaIdent));
                // Log why user disconnected if it was from an exception.
                if (exception is not null)
                    _logger.LogCallWarning(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), Context.ConnectionId, exception.Message, exception.StackTrace ?? string.Empty));

                // Remove from the redis database and send offline to all of their pairs.
                await RemoveUserFromRedis().ConfigureAwait(false);
                await SendOfflineToAllPairedUsers().ConfigureAwait(false);
            }
            catch { /* Consume */ }
            finally
            {
                // Remove from user connections and any radar chats.
                await _radarService.LeaveRadarChat(Context.ConnectionId).ConfigureAwait(false);
                _userConnections.Remove(UserUID, out string removedId);
                await _radarService.LeaveRadarChat(removedId).ConfigureAwait(false);
            }
        }
        // Log warning if a DC happened before we were even properly established in _userConnections.
        else
        {
            _logger.LogCallWarning(SundouleiaHubLogger.Args(_contextAccessor.GetIpAddress(), "ObsoleteId", UserUID, Context.ConnectionId));
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
}
#pragma warning restore MA0011


