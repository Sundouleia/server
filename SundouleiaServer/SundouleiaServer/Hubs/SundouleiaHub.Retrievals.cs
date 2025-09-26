using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace SundouleiaServer.Hubs;
#nullable enable

public partial class SundouleiaHub
{
    [Authorize(Policy = "Identified")]
    public async Task<List<OnlineUser>> UserGetOnlinePairs()
    {
        _logger.LogCallInfo();
        var allPairedUsers = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var pairs = await GetOnlineUsers(allPairedUsers).ConfigureAwait(false);
        // send that you are online to all connected online pairs of the client caller.
        await SendOnlineToAllPairedUsers().ConfigureAwait(false);
        // then, return back to the client caller the list of all users that are online in their client pairs.
        return pairs.Select(p => new OnlineUser(new(p.Key), p.Value)).ToList();
    }

    [Authorize(Policy = "Identified")]
    public async Task<List<UserPair>> UserGetAllPairs()
    {
        var pairs = await GetAllPairInfo(UserUID).ConfigureAwait(false);
        // Convert the UserInfo objects returned into UserPair DTO's for transit.
        return pairs.Select(p =>
        {
            var pairList = new UserPair(
                new UserData(p.Key, p.Value.Alias, p.Value.Tier, p.Value.Created),
                p.Value.OwnPerms.ToApi(),
                p.Value.OtherGlobals.ToApi(),
                p.Value.OtherPerms.ToApi()
            );
            return pairList;
        }).ToList();
    }

    /// <summary>
    ///     Returns ALL Active requests associated with the caller contextUID. <para />
    ///     As such, the pending requests returned could be made by the caller, or with 
    ///     the context caller as the recipient.
    /// </summary>
    
    [Authorize(Policy = "Identified")]
    public async Task<List<PendingRequest>> UserGetPendingRequests()
        => await DbContext.Requests.AsNoTracking()
        .Where(k => k.UserUID == UserUID || k.OtherUserUID == UserUID)
        .Select(r => r.ToApi())
        .ToListAsync()
        .ConfigureAwait(false);

    /// <summary>
    ///     Retrieves the Profile the requested user. <para />
    ///     Based on the Profiles permissions, and the context caller,
    ///     the profile data may vary for privacy purposes.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<FullProfileData> UserGetProfileData(UserDto user)
    {
        // Obtain the auth to know if they are allowed to view the profile to begin with, and if the caller is legit.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(a => a.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return new FullProfileData(user.User, new ProfileContent(), string.Empty);

        // If bad rep, return bad rep profile.
        if (!auth.AccountRep.ProfileViewing)
            return new FullProfileData(user.User, new ProfileContent() { Description = "Your Account Reputation prevents you from viewing profiles." }, string.Empty);

        // Get the pairs of the context caller.
        var callerPairs = await GetPairedUnpausedUsers().ConfigureAwait(false);
        // Obtain the profile data entry from the DBContext.
        var profile = await DbContext.UserProfileData.AsNoTracking().SingleAsync(u => u.UserUID == user.User.UID).ConfigureAwait(false);

        // If the profile is marked as non-public, and not the callers profile, return a private profile.
        if (!callerPairs.Contains(user.User.UID, StringComparer.Ordinal) && !string.Equals(user.User.UID, UserUID, StringComparison.Ordinal) && !profile.IsPublic)
            return new FullProfileData(user.User, new ProfileContent() { Description = "Profile is not Public!" }, string.Empty);

        // If the profile is disabled by moderation, return that it is disabled.
        if (profile.IsDisabled)
            return new FullProfileData(user.User, new ProfileContent() { Disabled = true, Description = "This profile is currently disabled" }, string.Empty);

        // Profile is valid so get the full profile data.
        var content = profile.FromProfileData();
        return new FullProfileData(user.User, content, profile.Base64AvatarData);
    }
}

