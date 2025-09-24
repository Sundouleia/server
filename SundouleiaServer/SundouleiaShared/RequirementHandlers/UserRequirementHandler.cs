using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using StackExchange.Redis.Extensions.Core.Abstractions;
using SundouleiaShared.Data;
using SundouleiaShared.Utils;

namespace SundouleiaShared.RequirementHandlers;

/// <summary>
///     Processes the user requirements when interacting with the servers.
/// </summary>
public class UserRequirementHandler : AuthorizationHandler<UserRequirement, HubInvocationContext>
{
    private readonly SundouleiaDbContext _dbContext;
    private readonly ILogger<UserRequirementHandler> _logger;
    private readonly IRedisDatabase _redis;

    public UserRequirementHandler(SundouleiaDbContext dbContext, ILogger<UserRequirementHandler> logger, IRedisDatabase redisDb)
    {
        _dbContext = dbContext;
        _logger = logger;
        _redis = redisDb;
    }

    /// <summary> Handles the requirement for the user. </summary>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, UserRequirement requirement, HubInvocationContext resource)
    {
        // output the requirements to the logger
        //_logger.LogInformation("Requirements for access to function: {requirements}", requirement.Requirements);
        // first before anything we should see if the context user contains the claims for temporary access. If they do, we should set the user requirements to identify them as a temporary access connection.
        if ((requirement.Requirements & UserRequirements.TempAccess) is UserRequirements.TempAccess)
        {
            bool hasCIdAccess = context.User.Claims.Any(c => string.Equals(c.Type, SundouleiaClaimTypes.AccessType, StringComparison.Ordinal)
                                                 && string.Equals(c.Value, "LocalContent", StringComparison.Ordinal));
            // if the user does not have temporary access, fail the context
            if (!hasCIdAccess)
                context.Fail();
        }


        // if the requirement is Identified, check if the UID is in the Redis database
        if ((requirement.Requirements & UserRequirements.Identified) is UserRequirements.Identified)
        {
            //_logger.LogInformation("Validating Identified requirement");
            // Get the UID from the context claim
            string uid = context.User.Claims.SingleOrDefault(g => string.Equals(g.Type, SundouleiaClaimTypes.Uid, StringComparison.Ordinal))?.Value;
            // if the UID is null, fail the context
            //_logger.LogInformation("SundouleiaHub:UID: {uid}", uid);

            if (uid is null)
                context.Fail();
            // fetch the ident(ity) from the Redis database

            //_logger.LogInformation("Fetching ident from Redis");

            // ar ident = await _redis.GetAsync<string>("SundouleiaHub:UID:" + uid).ConfigureAwait(false);
            string ident = await _redis.GetAsync<string>("SundouleiaHub:UID:" + uid).ConfigureAwait(false);
            if (ident == RedisValue.EmptyString)
                context.Fail();
        }

        // otherwise, succeed the context requirement.
        context.Succeed(requirement);
    }
}
