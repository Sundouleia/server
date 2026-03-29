using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Data;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;

namespace SundouleiaServer.Hubs;
#pragma warning disable MA0016, CS8619
#nullable enable
public partial class SundouleiaHub
{
    protected static readonly string[] ValidFileTypes = { ".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk" };

    // The context user claims associated with signalR callers for Sundouleia
    public string UserCharaIdent => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.CharaIdent, StringComparison.Ordinal))?.Value ?? throw new Exception("No Chara Ident in Claims");
    public string UserUID => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.Uid, StringComparison.Ordinal))?.Value ?? throw new Exception("No UID in Claims");
    public string UserHasTempAccess => Context.User?.Claims?.SingleOrDefault(c => string.Equals(c.Type, SundouleiaClaimTypes.AccessType, StringComparison.Ordinal))?.Value ?? throw new Exception("No TempAccess in Claims");

    #region Filehost Helpers
    /// <summary>
    ///     Requests the file download links from the file host for the specified mod files. <para />
    ///     The FileHost will return download links for any that exist, and authorized upload links for those that do not. <para />
    ///     These are both returned in the resulting record, and should be handled accordingly.
    /// </summary>
    private async Task<ModFileUrlResult> RequestFiles(List<FileHashData> newMods)
    {
        // These contain the authorized upload links to send back to the client caller.
        var requiresUpload = new List<ValidFileHash>();
        var validMods = new List<ValidFileHash>();

        var urlInfo = await _fileHost.GetUploadUrlsAsync(newMods.Select(m => m.Hash)).ConfigureAwait(false);

        // Iterate through all of the new mods and filter them accordingly based on their resulting dictionary outcomes.
        foreach (var modToAdd in newMods)
        {
            if (urlInfo.DownloadUrl.TryGetValue(modToAdd.Hash, out var dlLink))
                validMods.Add(new(modToAdd.Hash, modToAdd.GamePaths, dlLink));
            else if (urlInfo.UploadUrl.TryGetValue(modToAdd.Hash, out var ulLink))
                requiresUpload.Add(new(modToAdd.Hash, modToAdd.GamePaths, ulLink));
        }
        // return the record containing the resulting lists.
        return new(validMods, requiresUpload);
    }
    #endregion Filehost Helpers


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
    private async Task<Dictionary<string, string>> GetOnlineUsers(IEnumerable<string> uids)
    {
        var result = await _redis.GetAllAsync<string>(uids.Select(u => "UID:" + u).ToHashSet(StringComparer.Ordinal)).ConfigureAwait(false);
        return uids.Where(u => result.TryGetValue("UID:" + u, out var ident) && !string.IsNullOrEmpty(ident)).ToDictionary(u => u, u => result["UID:" + u], StringComparer.Ordinal);
    }

    /// <summary>
    ///     Helper function to get the user's identity from the redis by their UID
    /// </summary>
    private async Task<string?> GetUserIdent(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return string.Empty;
        return await _redis.GetAsync<string>("UID:" + uid).ConfigureAwait(false);
    }

    /// <summary>
    ///     Helper function to remove a user from the redis by their UID
    /// </summary>
    private async Task RemoveUserFromRedis()
    {
        await _redis.RemoveAsync("UID:" + UserUID, StackExchange.Redis.CommandFlags.FireAndForget).ConfigureAwait(false);
    }

    /// <summary>
    ///     A helper function to update the user's identity on the redi's by their UID
    /// </summary>
    private async Task UpdateUserOnRedis()
    {
        await _redis.AddAsync("UID:" + UserUID, UserCharaIdent, TimeSpan.FromSeconds(60),
            StackExchange.Redis.When.Always, StackExchange.Redis.CommandFlags.FireAndForget).ConfigureAwait(false);
    }

    private async Task<List<string>> SendIsUnloadingToAllPairedUsers()
    {
        var pairedUids = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var self = await DbContext.Users.AsNoTracking().SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        await Clients.Users(pairedUids).Callback_UserIsUnloading(new(self.ToUserData())).ConfigureAwait(false);
        return pairedUids;
    }

    /// <summary> 
    ///     Sends Callback_UserOffline to all paired online users of the client caller.
    /// </summary>
    /// <returns> The list of UID's the callbacks were sent to. </returns>
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
                              UserUID = cp.UserUID,             // Select UserUID
                              OtherUserUID = cp.OtherUserUID,   // Select OtherUserUID
                              InitializedAt = cp.CreatedAt,     // When the pairing was created
                              TempAccepter = cp.TempAccepterUID,// If temporary, who accepted it.
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
                                OtherUID = user.OtherUserUID,
                                OtherAlias = u.Alias,
                                OtherDispName = u.DisplayName,
                                OtherColor = u.NameColor,
                                OtherGlow = u.NameGlowColor,
                                OtherTier = u.Tier,
                                OtherCreatedDate = u.CreatedAt,
                                OwnGlobalPerms = userGlobalPerm,
                                OwnPermissions = ownperm,
                                OtherGlobalPerms = otherUserGlobalPerm,
                                OtherPermissions = otherperm,
                                InitializedAt = user.InitializedAt,
                                TempAccepter = user.TempAccepter,
                            };

        // Get final query result using no tracking.
        var res = await resultingInfo.AsNoTracking().ToListAsync().ConfigureAwait(false);
        if (res.Count is 0)
            return null;

        var resUserDat = new UserData(
            res[0].OtherUID,
            res[0].OtherAlias,
            res[0].OtherDispName,
            res[0].OtherColor,
            res[0].OtherGlow,
            res[0].OtherTier,
            res[0].OtherCreatedDate
        );

        // Construct and return the UserInfo object.
        return new UserInfo(
            resUserDat,
            res[0].OwnGlobalPerms,
            res[0].OwnPermissions,
            res[0].OtherGlobalPerms,
            res[0].OtherPermissions,
            res[0].InitializedAt,
            res[0].TempAccepter
        );
    }

    public async Task<Dictionary<string, RequestInfo>> GetRespondingRequests(IEnumerable<string> uids)
    {
        var targets = uids.ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0)
            return new Dictionary<string, RequestInfo>(StringComparer.Ordinal);

        var info =  from r in DbContext.Requests.AsNoTracking().Where(r => r.OtherUserUID == UserUID && targets.Contains(r.UserUID))
                    join sender in DbContext.Users.AsNoTracking()
                        on r.UserUID equals sender.UID
                    join senderGlobals in DbContext.UserGlobalPerms.AsNoTracking()
                        on r.UserUID equals senderGlobals.UserUID
                    join targetGlobals in DbContext.UserGlobalPerms.AsNoTracking()
                        on UserUID equals targetGlobals.UserUID
                    select new
                    {
                        TargetUid = r.UserUID,
                        Sender = sender,
                        Request = r,
                        SenderGlobals = senderGlobals,
                        TargetGlobals = targetGlobals
                    };

        var infoResult = await info.ToListAsync().ConfigureAwait(false);

        // Might need to reformat to work more like below.
        return infoResult.ToDictionary(x => x.TargetUid, 
            x => new RequestInfo(x.Request, x.Sender, x.SenderGlobals, x.TargetGlobals), StringComparer.Ordinal);
    }

    private async Task<List<User>> GetRequestableUsers(IEnumerable<string> userUids)
    {
        // convert to hashset for quick matching.
        var targets = userUids.ToHashSet(StringComparer.Ordinal);
        if (targets.Count is 0)
            return [];

        var query = from u in DbContext.Users.AsNoTracking()
                    where targets.Contains(u.UID) || targets.Contains(u.Alias)
                    // Deny if blocked
                    where !DbContext.BlacklistedUsers.AsNoTracking().Any(b => (b.UserUID == UserUID && b.BlockedUserUID == u.UID) || (b.UserUID == u.UID && b.BlockedUserUID == UserUID))
                    // Deny if exists
                    where !DbContext.Requests.AsNoTracking().Any(r => (r.UserUID == UserUID && r.OtherUserUID == u.UID) || (r.UserUID == u.UID && r.OtherUserUID == UserUID))
                    // Deny if paired
                    where !DbContext.ClientPairs.AsNoTracking().Any(p => (p.UserUID == UserUID && p.OtherUserUID == u.UID) || (p.UserUID == u.UID && p.OtherUserUID == UserUID))
                    select u;
        // ret the query result as a list.
        return await query.ToListAsync().ConfigureAwait(false);
    }


    private async Task<Dictionary<string, PairRequest>> GetSentRequests(IEnumerable<string> uids)
    {
        var targets = uids.ToHashSet(StringComparer.Ordinal);
        // Query all existing requests in one go
        var requestsQuery = from req in DbContext.Requests.AsNoTracking()
                            join tgt in DbContext.Users.AsNoTracking() 
                            on req.OtherUserUID equals tgt.UID 
                            where req.UserUID == UserUID && targets.Contains(req.OtherUserUID)
                            select new
                            {
                                Request = req,
                                TargetUid = tgt.UID,
                                TargetAlias = tgt.Alias,
                                TargetCreatedAt = tgt.CreatedAt
                            };

        var requestsList = await requestsQuery.ToListAsync().ConfigureAwait(false);

        // Form dictionary keyed by target UID
        return requestsList.ToDictionary(x => x.TargetUid, x => x.Request, StringComparer.Ordinal);
    }


    // --- Prototype, untested model --- 
    //private async Task<Dictionary<string, UserInfo>> GetAllPairInfo(string uid)
    //{
    //    // Directly join everything and project into DTOs
    //    var query =
    //        from cp in DbContext.ClientPairs.AsNoTracking().Where(c => c.UserUID == uid)
    //        join u in DbContext.Users.AsNoTracking() on cp.OtherUserUID equals u.UID
    //        join own in DbContext.ClientPairPerms.AsNoTracking().Where(p => p.UserUID == uid)
    //            on new { cp.UserUID, cp.OtherUserUID } equals new { own.UserUID, own.OtherUserUID }
    //            into ownPerms
    //        from ownperm in ownPerms.DefaultIfEmpty()
    //        join other in DbContext.ClientPairPerms.AsNoTracking().Where(p => p.OtherUserUID == uid)
    //            on new { cp.UserUID, cp.OtherUserUID } equals new { UserUID = other.OtherUserUID, OtherUserUID = other.UserUID }
    //            into otherPerms
    //        from otherperm in otherPerms.DefaultIfEmpty()
    //        join ug in DbContext.UserGlobalPerms.AsNoTracking() on cp.UserUID equals ug.UserUID into userGlobalPerms
    //        from userGlobalPerm in userGlobalPerms.DefaultIfEmpty()
    //        join oug in DbContext.UserGlobalPerms.AsNoTracking() on cp.OtherUserUID equals oug.UserUID into otherUserGlobalPerms
    //        from otherUserGlobalPerm in otherUserGlobalPerms.DefaultIfEmpty()
    //        select new UserInfo(
    //            new UserData(u.UID, u.Alias, u.DisplayName, u.NameColor, u.NameGlowColor, u.Tier, u.CreatedAt),
    //            userGlobalPerm,
    //            ownperm,
    //            otherUserGlobalPerm,
    //            otherperm,
    //            cp.CreatedAt,
    //            cp.TempAccepterUID
    //        );
    //    var list = await query.ToListAsync().ConfigureAwait(false);
    //    // Dictionary keyed by OtherUserUID
    //    return list.ToDictionary(info => info.UserData.UID, info => info, StringComparer.Ordinal);
    //}


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
                                InitializedAt = cp.CreatedAt,     // When the pairing was created
                                TempAccepter = cp.TempAccepterUID,// If temporary, who accepted it.
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
                                OtherAlias = u.Alias,
                                OtherDispName = u.DisplayName,
                                OtherColor = u.NameColor,
                                OtherGlow = u.NameGlowColor,
                                OtherTier = u.Tier,
                                OtherCreatedDate = u.CreatedAt,
                                OwnGlobalPerms = userGlobalPerm,
                                OwnPermissions = ownperm,
                                OtherGlobalPerms = otherUserGlobalPerm,
                                OtherPermissions = otherperm,
                                InitializedAt = user.InitializedAt,
                                TempAccepter = user.TempAccepter,
                            };

        // obtain the query result and form it into an established list
        var resultList = await resultingInfo.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Group results by OtherUserUID and convert to dictionary for return
        return resultList
            .GroupBy(g => g.OtherUserUID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.First();
                var userData = new UserData(
                    first.OtherUserUID,
                    first.OtherAlias,
                    first.OtherDispName,
                    first.OtherColor,
                    first.OtherGlow,
                    first.OtherTier,
                    first.OtherCreatedDate
                );
                var userInfo = new UserInfo(
                    userData,
                    first.OwnGlobalPerms,
                    first.OwnPermissions,
                    first.OtherGlobalPerms,
                    first.OtherPermissions,
                    first.InitializedAt,
                    first.TempAccepter
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

    public record RequestInfo(PairRequest Request, User Sender, GlobalPermissions SenderGlobals, GlobalPermissions RecipientGlobals);

    public record UserInfo(UserData UserData, GlobalPermissions OwnGlobals, ClientPairPermissions OwnPerms, 
        GlobalPermissions OtherGlobals, ClientPairPermissions OtherPerms, DateTime PairInitAt, string PairTempAccepter);
}
#pragma warning restore MA0016
#nullable disable
