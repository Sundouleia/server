using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;

namespace SundouleiaServer.Hubs;
#pragma warning disable MA0016, CS8619
#nullable enable
public partial class SundouleiaHub
{
    // The context user claims associated with signalR callers for Sundouleia
    public string UserCharaIdent => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.CharaIdent, StringComparison.Ordinal))?.Value ?? throw new Exception("No Chara Ident in Claims");
    public string UserUID => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.Uid, StringComparison.Ordinal))?.Value ?? throw new Exception("No UID in Claims");
    public string UserHasTempAccess => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.AccessType, StringComparison.Ordinal))?.Value ?? throw new Exception("No TempAccess in Claims");

    /// <summary>
    ///     Gets all unpaused pairs of <paramref name="uid"/>. <para />
    ///     If no UID is provided, assumes the caller context UID.
    /// </summary>
    private async Task<List<string>> GetPairedUnpausedUsers(string? uid = null)
    {
        uid ??= UserUID;
        return (await GetUnpausedOnlinePairs(uid).ConfigureAwait(false));
    }

    /// <summary>
    ///     Helper to get the total number of users who are online currently from the list of passed in UID's.
    /// </summary>
    private async Task<Dictionary<string, string>> GetOnlineUsers(List<string> uids)
    {
        var result = await _redis.GetAllAsync<string>(uids.Select(u => "SundouleiaHub:UID:" + u).ToHashSet(StringComparer.Ordinal)).ConfigureAwait(false);
        return uids.Where(u => result.TryGetValue("SundouleiaHub:UID:" + u, out var ident) && !string.IsNullOrEmpty(ident)).ToDictionary(u => u, u => result["SundouleiaHub:UID:" + u], StringComparer.Ordinal);
    }

    /// <summary>
    ///     Helper function to get the user's identity from the redis by their UID
    /// </summary>
    private async Task<string?> GetUserIdent(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return string.Empty;
        return await _redis.GetAsync<string>("SundouleiaHub:UID:" + uid).ConfigureAwait(false);
    }

    /// <summary>
    ///     Helper function to remove a user from the redis by their UID
    /// </summary>
    private async Task RemoveUserFromRedis()
    {
        await _redis.RemoveAsync("SundouleiaHub:UID:" + UserUID, StackExchange.Redis.CommandFlags.FireAndForget).ConfigureAwait(false);
    }

    /// <summary>
    ///     A helper function to update the user's identity on the redi's by their UID
    /// </summary>
    private async Task UpdateUserOnRedis()
    {
        await _redis.AddAsync("SundouleiaHub:UID:" + UserUID, UserCharaIdent, TimeSpan.FromSeconds(60),
            StackExchange.Redis.When.Always, StackExchange.Redis.CommandFlags.FireAndForget).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Sends Callback_UserOffline to all paired online users of the client caller.
    /// </summary>
    /// <returns>
    ///     The list of UID's the callbacks were sent to.
    /// </returns>
    private async Task<List<string>> SendOfflineToAllPairedUsers()
    {
        var pairedUids = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var self = await DbContext.Users.AsNoTracking().SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        await Clients.Users(pairedUids).Callback_UserOffline(new(self.ToUserData())).ConfigureAwait(false);
        return pairedUids;
    }

    /// <summary> 
    ///     Sends Callback_UserOnline to all paired online users of the client caller.
    /// </summary>
    /// <returns>
    ///     The list of UID's the callbacks were sent to.
    /// </returns>
    private async Task<List<string>> SendOnlineToAllPairedUsers()
    {
        var pairedUids = await GetPairedUnpausedUsers().ConfigureAwait(false);
        _logger.LogMessage($"{UserUID} had {pairedUids.Count} paired unpaused users: {string.Join(", ", pairedUids)}");
        var self = await DbContext.Users.AsNoTracking().SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        await Clients.Users(pairedUids).Callback_UserOnline(new(self.ToUserData(), UserCharaIdent)).ConfigureAwait(false);
        return pairedUids;
    }

    /// <summary>
    ///     Whenever the discord bot adds a new verification attempt (AuthClaim) entry to the DB,
    ///     it is intercepted by a dbNotificationListener that will then call this method after 
    ///     the entry is added into the DB. <para />
    ///     
    ///     In Short, this is responsible for how we determine who gets what verification code, 
    ///     and when to display it on screen.
    /// </summary>
    public async Task DisplayVerificationCodes(List<AccountClaimAuth> newClaimAuths)
    {
        _logger.LogMessage("Displaying verification codes to users");
        foreach (var claimAuth in newClaimAuths)
        {
            // Identify the auth with the patching hashed key.
            var auth = await DbContext.Auth.AsNoTracking().SingleAsync(u => u.HashedKey == claimAuth.InitialGeneratedKey).ConfigureAwait(false);

            // If the user is online, prompt the verification code to them.
            if (_userConnections.ContainsKey(auth.UserUID))
                await Clients.User(auth.UserUID).Callback_ShowVerification(new() { Code = claimAuth.VerificationCode ?? "" }).ConfigureAwait(false);
        }
    }

    /// <summary> 
    ///     Retrieves the UserInfo record outlining essential information about the connection 
    ///     of two paired individuals. (Does not care if paused or not) <para />
    ///     It is a chonky database query.
    /// </summary>
    /// <returns> the UserInfo record containing info of the connection between the two individuals. Null if not paired. </returns>
    private async Task<UserInfo?> GetPairInfo(string uid, string otherUid)
    {
        // collect row(s) where userUID == uid && otherUserUID == otheruid (( should always be true since we added just prior ))
        var clientPairs = from cp in DbContext.ClientPairs.AsNoTracking().Where(u => u.UserUID == uid && u.OtherUserUID == otherUid)
                          // join the cp2 table which is defined by the collected row(s) where userUID == otherUid && otherUserUID == uid
                          join cp2 in DbContext.ClientPairs.AsNoTracking().Where(u => u.OtherUserUID == uid && u.UserUID == otherUid)
                          // this result is joined only when 
                          on new // this created object, aka our client pair object we made prior to calling this 
                          {
                              UserUID = cp.UserUID, // Create an anonymous object with UserUID
                              OtherUserUID = cp.OtherUserUID // and OtherUserUID
                          }
                          equals new // matches the other user's client pair object that they would have generated if they added us.
                          {
                              UserUID = cp2.OtherUserUID, // Join where the UserUID matches OtherUserUID of cp2
                              OtherUserUID = cp2.UserUID // and OtherUserUID matches UserUID of cp2
                          } into joined // if this joined variable is empty or default, it means they have not added us back, so we are not synced.
                          from c in joined.DefaultIfEmpty() // Therefore, we can use this result to determine if we are synced.
                          where cp.UserUID == uid // ((((Ensure we are only working with the given uid while doing this btw.))))
                          // and now we can make a new object with the results of this query.
                          select new //  [ Ultimately stored into clientPairs ]
                          {
                              UserUID = cp.UserUID, // Select UserUID
                              OtherUserUID = cp.OtherUserUID, // Select OtherUserUID
                              IsTemporary = cp.IsTemporary,
                          };

        if (!clientPairs.Any())
            return null;

        // Start by selecting from a previously defined collection 'clientPairs'
        var resultingInfo = from user in clientPairs
                            // Join with the Users table to get details of the "other user" in each pair
                            join u in DbContext.Users.AsNoTracking() on user.OtherUserUID equals u.UID
                            // Attempt to join with the ClientPairPermissions table to find permissions the user set for the other user
                            join o in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.UserUID == uid)
                                on new { UserUID = user.UserUID, OtherUserUID = user.OtherUserUID }
                                equals new { UserUID = o.UserUID, OtherUserUID = o.OtherUserUID } into ownperms
                            // find perms that uid has set for other user in the pair. Groups results into 'ownperms'
                            from ownperm in ownperms.DefaultIfEmpty()
                                // Now, attempt to find permissions set by the other user for the main user
                            join p in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.OtherUserUID == uid)
                                on new { UserUID = user.OtherUserUID, OtherUserUID = user.UserUID }
                                equals new { UserUID = p.UserUID, OtherUserUID = p.OtherUserUID } into otherperms
                            // find perms that the other user has set for the main user. Groups results into 'otherperms'
                            from otherperm in otherperms.DefaultIfEmpty()
                                // Join for GlobalPerms for the main user
                            join ug in DbContext.UserGlobalPerms.AsNoTracking() on user.UserUID equals ug.UserUID into userGlobalPerms
                            from userGlobalPerm in userGlobalPerms.DefaultIfEmpty()
                                // Join for GlobalPerms for the other user
                            join oug in DbContext.UserGlobalPerms.AsNoTracking() on user.OtherUserUID equals oug.UserUID into otherUserGlobalPerms
                            from otherUserGlobalPerm in otherUserGlobalPerms.DefaultIfEmpty()
                            // Filter to include only pairs where the main user is involved
                            where user.UserUID == uid && u.UID == user.OtherUserUID
                            // Select the final projection of data to include in the results
                            select new
                            {
                                UserUID = user.UserUID,
                                OtherUserUID = user.OtherUserUID,
                                OtherUserAlias = u.Alias,
                                OtherUserTier = u.Tier,
                                OtherUserCreatedDate = u.CreatedAt,
                                IsTemporary = user.IsTemporary,
                                OwnGlobalPerms = userGlobalPerm,
                                OwnPermissions = ownperm,
                                OtherGlobalPerms = otherUserGlobalPerm,
                                OtherPermissions = otherperm,
                            };

        // Get final query result using no tracking.
        var resultList = await resultingInfo.AsNoTracking().ToListAsync().ConfigureAwait(false);
        if (resultList.Count == 0)
            return null;

        // Construct and return the UserInfo object.
        return new UserInfo(
            resultList[0].OtherUserAlias,
            resultList[0].OtherUserTier,
            resultList[0].OtherUserCreatedDate,
            resultList[0].IsTemporary,
            resultList[0].OwnGlobalPerms,
            resultList[0].OwnPermissions,
            resultList[0].OtherGlobalPerms,
            resultList[0].OtherPermissions
        );
    }


    /// <summary>
    ///     Helper function to retrieve the UserInfo's for ALL pairs of a specific UID. <para />
    ///     Does not care about pause status.
    /// </summary>
    private async Task<Dictionary<string, UserInfo>> GetAllPairInfo(string uid)
    {
        // refer to GETPAIRINFO function above to see explanation of this function.
        var clientPairs =   from cp in DbContext.ClientPairs.AsNoTracking().Where(u => u.UserUID == uid)
                            join cp2 in DbContext.ClientPairs.AsNoTracking().Where(u => u.OtherUserUID == uid)
                            on new
                            {
                                UserUID = cp.UserUID,
                                OtherUserUID = cp.OtherUserUID
                            }
                            equals new
                            {
                                UserUID = cp2.OtherUserUID,
                                OtherUserUID = cp2.UserUID
                            } into joined
                            from c in joined.DefaultIfEmpty()
                            where cp.UserUID == uid
                            select new
                            {
                                UserUID = cp.UserUID,
                                OtherUserUID = cp.OtherUserUID,
                                IsTemporary = cp.IsTemporary,
                            };

        // Obtain the permission info for these pairs.
        // each 'clientPairs' item is the final resulting query of { UserUID, OtherUserUID }
        var resultingInfo = from user in clientPairs // <-- Define the above query as 'user'
                            // Join with Users table for the 'other user'
                            join u in DbContext.Users.AsNoTracking() 
                                on user.OtherUserUID equals u.UID

                            // Perform a Group join: this collects all ClientPairPermissions that UserUID set for OtherUserUID
                            join o in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.UserUID == uid)
                                on new { UserUID = user.UserUID, OtherUserUID = user.OtherUserUID }
                                equals new { UserUID = o.UserUID, OtherUserUID = o.OtherUserUID }
                                into ownperms
                            from ownperm in ownperms.DefaultIfEmpty()

                            // Perform a Group join: this collects all ClientPairPermissions that OtherUserUID set for UserUID
                            join p in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.OtherUserUID == uid)
                                on new { UserUID = user.OtherUserUID, OtherUserUID = user.UserUID }
                                equals new { UserUID = p.UserUID, OtherUserUID = p.OtherUserUID } 
                                into otherperms
                            from otherperm in otherperms.DefaultIfEmpty()

                            // Join for GlobalPerms for the main user
                            join ug in DbContext.UserGlobalPerms.AsNoTracking() 
                                on user.UserUID equals ug.UserUID into userGlobalPerms
                            from userGlobalPerm in userGlobalPerms.DefaultIfEmpty()
                            
                            // Join for GlobalPerms for the other user
                            join oug in DbContext.UserGlobalPerms.AsNoTracking()
                                on user.OtherUserUID equals oug.UserUID into otherUserGlobalPerms
                            from otherUserGlobalPerm in otherUserGlobalPerms.DefaultIfEmpty()

                            // Filter to include only pairs where the main user is involved
                            where user.UserUID == uid
                                && u.UID == user.OtherUserUID
                                && ownperm.UserUID == user.UserUID && ownperm.OtherUserUID == user.OtherUserUID
                                && (otherperm == null || (otherperm.UserUID == user.OtherUserUID && otherperm.OtherUserUID == user.UserUID))
                            // Select the final projection of data to include in the results
                            select new
                            {
                                UserUID = user.UserUID,
                                OtherUserUID = user.OtherUserUID,
                                IsTemporary = user.IsTemporary,
                                OtherUserAlias = u.Alias,
                                OtherUserVanity = u.Tier,
                                OtherUserCreatedDate = u.CreatedAt,
                                OwnGlobalPerms = userGlobalPerm,
                                OwnPermissions = ownperm,
                                OtherGlobalPerms = otherUserGlobalPerm,
                                OtherPermissions = otherperm,
                            };

        // obtain the query result and form it into an established list
        var resultList = await resultingInfo.AsNoTracking().ToListAsync().ConfigureAwait(false);


        // Group results by OtherUserUID and convert to dictionary for return
        return resultList.GroupBy(g => g.OtherUserUID, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
        {
            // for some unexplainable reason, putting a return where the var is makes this no longer work.
            // I dont fucking know why, it just doesn't.
            var userInfo = new UserInfo(
                g.First().OtherUserAlias,
                g.First().OtherUserVanity,
                g.First().OtherUserCreatedDate,
                g.First().IsTemporary,
                g.First().OwnGlobalPerms,
                g.First().OwnPermissions,
                g.First().OtherGlobalPerms,
                g.First().OtherPermissions
            );
            return userInfo;
        }, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Given a defined uid, retrieve all of this uid's paired users uid's who are online and not paused.
    /// </summary>
    private async Task<List<string>> GetUnpausedOnlinePairs(string uid)
    {
        var clientPairs = from cp in DbContext.ClientPairs.AsNoTracking().Where(u => u.UserUID == uid)
                          join cp2 in DbContext.ClientPairs.AsNoTracking().Where(u => u.OtherUserUID == uid)
                          on new
                          {
                              UserUID = cp.UserUID,
                              OtherUserUID = cp.OtherUserUID
                          }
                          equals new
                          {
                              UserUID = cp2.OtherUserUID,
                              OtherUserUID = cp2.UserUID
                          } into joined
                          from c in joined.DefaultIfEmpty()
                          where cp.UserUID == uid && c.UserUID != null
                          select new
                          {
                              UserUID = cp.UserUID,
                              OtherUserUID = cp.OtherUserUID,
                          };

        // Start by selecting from a previously defined collection 'clientPairs'
        var resultingInfo = 
            from user in clientPairs
                // Join with the Users table to get details of the "other user" in each pair
            join u in DbContext.Users.AsNoTracking() on user.OtherUserUID equals u.UID
            // Attempt to join with the ClientPairPermissions table to find permissions the user set for the other user
            join o in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.UserUID == uid)
                on new { UserUID = user.UserUID, OtherUserUID = user.OtherUserUID }
                equals new { UserUID = o.UserUID, OtherUserUID = o.OtherUserUID } into ownperms
            // find perms that uid has set for other user in the pair. Groups results into 'ownperms'
            from ownperm in ownperms.DefaultIfEmpty()

                // Now, attempt to find permissions set by the other user for the main user
            join p in DbContext.ClientPairPerms.AsNoTracking().Where(u => u.OtherUserUID == uid)
                on new { UserUID = user.OtherUserUID, OtherUserUID = user.UserUID }
                equals new { UserUID = p.UserUID, OtherUserUID = p.OtherUserUID } into otherperms
            // find perms that the other user has set for the main user. Groups results into 'otherperms'
            from otherperm in otherperms.DefaultIfEmpty()

                // Filter to include only pairs where the main user is involved
            where user.UserUID == uid
                && ownperm.UserUID == user.UserUID && ownperm.OtherUserUID == user.OtherUserUID
                && otherperm.OtherUserUID == user.UserUID && otherperm.UserUID == user.OtherUserUID
                && !ownperm.PauseVisuals && (otherperm == null ? false : !otherperm.PauseVisuals)

            // Select the UID of this outcome.
            select user.OtherUserUID;

        // Final result with be a distinct list of this outcome.
        return await resultingInfo.Distinct().AsNoTracking().ToListAsync().ConfigureAwait(false);
    }


    public record UserInfo(string Alias, CkVanityTier Tier, DateTime Created, bool IsTemporary,
        GlobalPermissions OwnGlobals, ClientPairPermissions OwnPerms, GlobalPermissions OtherGlobals, ClientPairPermissions OtherPerms);
}
#pragma warning restore MA0016
#nullable disable
