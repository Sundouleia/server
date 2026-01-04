using SundouleiaShared.Data;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SundouleiaShared.Utils;
#nullable enable

/// <summar> Quality of Life helper class for shared database functions </summary>
public static class SharedDbFunctions
{
    /// <summary>
    ///     Purges a user completely from the database, including all secondary users associated with them. <para />
    ///     This means if they are an alt account, or a main account, they are all removed. <para />
    ///     It is wise to only do this when banning someone. Should not be used on other things like cleanup.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the auth is null.</exception>
    public static async Task PurgeUserAccount(User user, ILogger logger, SundouleiaDbContext dbContext, SundouleiaMetrics? metrics = null)
    {
        logger.LogInformation("Purging user account: {uid}", user.UID);
        // grab all profiles of the account.
        var altProfiles = await dbContext.Auth.AsNoTracking().Include(u => u.User).Where(u => u.PrimaryUserUID == user.UID).Select(c => c.User).ToListAsync().ConfigureAwait(false);
        foreach (var userProfile in altProfiles)
        {
            logger.LogDebug($"Located Alt Profile: {userProfile.UID} (Alias: {userProfile.Alias}), Purging them first.");
            await DeleteUserProfile(userProfile, logger, dbContext, metrics).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Removes a single profile of a user from the database. <para />
    ///     <b>IF THE PROFILE BEING REMOVED IS THE PRIMARY ACCOUNT PROFILE, ALL ALT PROFILES ARE REMOVED.</b><para />
    /// </summary>
    /// <returns> a dictionary linking the removed userUID's to the list of UID's paired with them. Dictionary is used incase primary profile is removed. </returns>
    /// <exception cref="ArgumentNullException">Thrown when the auth is null.</exception>
    public static async Task<Dictionary<string, List<string>>>  DeleteUserProfile(User user, ILogger logger, SundouleiaDbContext dbContext, SundouleiaMetrics? metrics = null)
    {
        var retDict = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // Obtain the caller's Auth entry, which contains the User entry inside.
        if (await dbContext.Auth.AsNoTracking().Include(a => a.User).SingleOrDefaultAsync(a => a.UserUID == user.UID).ConfigureAwait(false) is not { } callerAuth)
            return retDict;

        // If PrimaryUserUID is null or empty, it is the primary profile, and we should remove all alt profiles.
        if (string.IsNullOrEmpty(callerAuth.PrimaryUserUID))
        {
            var altProfiles = await dbContext.Auth.AsNoTracking().Include(u => u.User).Where(u => u.PrimaryUserUID == user.UID).Select(c => c.User).ToListAsync().ConfigureAwait(false);
            foreach (var altProfile in altProfiles)
            {
                var pairedUids = await DeleteProfileInternal(callerAuth.User, logger, dbContext, metrics).ConfigureAwait(false);
                retDict.Add(callerAuth.User.UID, pairedUids);
            }
        }
        // Remove the primary profile.
        var pairedMainUids = await DeleteProfileInternal(callerAuth.User, logger, dbContext, metrics).ConfigureAwait(false);
        retDict.Add(callerAuth.User.UID, pairedMainUids);

        // return the dictionary of removed profiles and their paired UID's.
        return retDict;
    }

    private static async Task<List<string>> DeleteProfileInternal(User user, ILogger logger, SundouleiaDbContext dbContext, SundouleiaMetrics? metrics = null)
    {
        // Account Data. (if auth fails to fetch, this should deservedly throw an exception!.
        var auth = dbContext.Auth.SingleAsync(a => a.UserUID == user.UID).ConfigureAwait(false);
        var accountClaim = dbContext.AccountClaimAuth.AsNoTracking().SingleOrDefault(a => a.User != null && a.User.UID == user.UID);
        var reputation = await dbContext.AccountReputation.AsNoTracking().SingleOrDefaultAsync(a => a.UserUID == user.UID).ConfigureAwait(false);
        // Blocked Users.
        var blockedUsers = await dbContext.BlockedUsers.AsNoTracking().Where(b => b.UserUID == user.UID || b.OtherUserUID == user.UID).ToListAsync().ConfigureAwait(false);
        // Pair Data
        var ownPairData = await dbContext.ClientPairs.AsNoTracking().Where(u => u.User.UID == user.UID).ToListAsync().ConfigureAwait(false);
        var otherPairData = await dbContext.ClientPairs.AsNoTracking().Where(u => u.OtherUserUID == user.UID).ToListAsync().ConfigureAwait(false);
        var pairPerms = await dbContext.ClientPairPerms.AsNoTracking().Where(u => u.UserUID == user.UID || u.OtherUserUID == user.UID).ToListAsync().ConfigureAwait(false);
        // Requests
        var requests = await dbContext.Requests.AsNoTracking().Where(u => u.UserUID == user.UID || u.OtherUserUID == user.UID).ToListAsync().ConfigureAwait(false);
        // Globals & State Data
        var globals = await dbContext.UserGlobalPerms.AsNoTracking().SingleOrDefaultAsync(u => u.UserUID == user.UID).ConfigureAwait(false);
        var userProfileData = await dbContext.UserProfileData.AsNoTracking().SingleOrDefaultAsync(u => u.UserUID == user.UID).ConfigureAwait(false);
        var radarInfo = await dbContext.UserRadarInfo.AsNoTracking().SingleOrDefaultAsync(u => u.UserUID == user.UID).ConfigureAwait(false);
        // Get User Pair List to output.
        var pairedUids = otherPairData.Select(p => p.UserUID);

        // Remove all associated.
        if (accountClaim is not null) dbContext.Remove(accountClaim);
        if (reputation is not null) dbContext.Remove(reputation);

        dbContext.RemoveRange(blockedUsers);

        dbContext.RemoveRange(ownPairData);
        dbContext.RemoveRange(otherPairData);
        dbContext.RemoveRange(pairPerms);
        if (globals is not null) dbContext.Remove(globals);
        if (userProfileData is not null) dbContext.Remove(userProfileData);
        if (radarInfo is not null) dbContext.Remove(radarInfo);

        // now that everything is finally gone, remove the auth & user.
        dbContext.Remove(auth);
        dbContext.Remove(user);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        if (metrics is not null) metrics.IncCounter(MetricsAPI.CounterDeletedVerifiedUsers);
        return pairedUids.ToList();
    }

    /// <summary>
    ///     Safely created the primary account profile for a <paramref name="user"/> with their 
    ///     associated <paramref name="rep"/> and <paramref name="auth"/> to the DB. <para />
    ///     At the same time, this method will generate entries in all other tables user-related data is necessary. <para />
    ///     This will help reduce connection bloat.
    /// </summary>
    public static async Task CreateMainProfile(User user, AccountReputation rep, Auth auth, ILogger logger, SundouleiaDbContext dbContext, SundouleiaMetrics? metrics = null)
    {
        logger.LogInformation($"Creating new profile for: {user.UID} (Alias: {user.Alias})");
        await dbContext.Users.AddAsync(user).ConfigureAwait(false);
        await dbContext.AccountReputation.AddAsync(rep).ConfigureAwait(false);
        await dbContext.Auth.AddAsync(auth).ConfigureAwait(false);

        // Create all other necessary tables for the user now that it is added successfully.
        await dbContext.UserGlobalPerms.AddAsync(new GlobalPermissions { UserUID = user.UID }).ConfigureAwait(false);
        await dbContext.UserProfileData.AddAsync(new UserProfileData { UserUID = user.UID }).ConfigureAwait(false);
        await dbContext.UserRadarInfo.AddAsync(new UserRadarInfo { UserUID = user.UID }).ConfigureAwait(false);

        logger.LogInformation($"[User {user.UID} (Alias: {user.Alias}) <{user.Tier}>] was created along with other necessary table entries!");
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates an alt profile for an account safely, along with their associated <paramref name="auth"/> to the DB. <para />
    ///     At the same time, this method will generate entries in all other tables user-related data is necessary. <para />
    ///     This will help reduce connection bloat.
    /// </summary>
    public static async Task CreateAltProfile(User user, Auth auth, ILogger logger, SundouleiaDbContext dbContext, SundouleiaMetrics? metrics = null)
    {
        logger.LogInformation($"Creating new profile for: {user.UID} (Alias: {user.Alias})");
        await dbContext.Users.AddAsync(user).ConfigureAwait(false);
        await dbContext.Auth.AddAsync(auth).ConfigureAwait(false);

        // Add AccountReputation if their primaryUid is their UserUid.
        if (string.Equals(user.UID, auth.PrimaryUserUID, StringComparison.Ordinal))
            await dbContext.AccountReputation.AddAsync(new AccountReputation { UserUID = user.UID }).ConfigureAwait(false);

        // Create all other necessary tables for the user now that it is added successfully.
        await dbContext.UserGlobalPerms.AddAsync(new GlobalPermissions { UserUID = user.UID }).ConfigureAwait(false);
        await dbContext.UserProfileData.AddAsync(new UserProfileData { UserUID = user.UID }).ConfigureAwait(false);
        await dbContext.UserRadarInfo.AddAsync(new UserRadarInfo { UserUID = user.UID }).ConfigureAwait(false);

        logger.LogInformation($"[User {user.UID} (Alias: {user.Alias}) <{user.Tier}>] was created along with other necessary table entries!");
        await dbContext.SaveChangesAsync().ConfigureAwait(false);
    }
}