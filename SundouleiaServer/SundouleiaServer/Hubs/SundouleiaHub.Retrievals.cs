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
        _logger.LogCallInfo();
        var pairs = await GetAllPairInfo(UserUID).ConfigureAwait(false);
        // Convert the UserInfo objects returned into UserPair DTO's for transit.
        return pairs.Select(p =>
        {
            var pairList = new UserPair(
                new UserData(p.Key, p.Value.Alias, p.Value.Tier, p.Value.Created),
                p.Value.OwnPerms.ToApi(),
                p.Value.OtherGlobals.ToApi(),
                p.Value.OtherPerms.ToApi(),
                p.Value.IsTemporary
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
    public async Task<List<SundesmoRequest>> UserGetSundesmoRequests()
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
    public async Task<FullProfileData> UserGetProfileData(UserDto user, bool allowNSFW)
    {
        // If requested profile matches the caller, return the full profile always.
        if (string.Equals(user.User.UID, UserUID, StringComparison.Ordinal))
        {
            var ownProfile = await DbContext.UserProfileData.AsNoTracking().SingleAsync(u => u.UserUID == UserUID).ConfigureAwait(false);
            return new FullProfileData(user.User, ownProfile.FromProfileData(), ownProfile.Base64AvatarData);
        }

        // Obtain the auth to know if they are allowed to view the profile to begin with, and if the caller is legit.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(a => a.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return new FullProfileData(user.User, new ProfileContent(), string.Empty);

        // If bad rep, return bad rep profile.
        if (!auth.AccountRep.ProfileViewing)
            return new FullProfileData(user.User, new ProfileContent() { Description = "Your Account Reputation prevents you from viewing profiles." }, string.Empty);


        // Profile is valid so get the full profile data.
        var profile = await DbContext.UserProfileData.AsNoTracking().SingleAsync(u => u.UserUID == user.User.UID).ConfigureAwait(false);
        var content = profile.FromProfileData();

        // Get the pairs of the context caller for the IsPublic check.
        if (!profile.IsPublic)
        {
            var callerPairs = await GetPairedUnpausedUsers().ConfigureAwait(false);
            if (!callerPairs.Contains(user.User.UID, StringComparer.Ordinal))
                return new FullProfileData(user.User, content with { Description = "Profile Pic is hidden as they have not allowed public plates!" }, string.Empty);
        }

        // If the profile is disabled by moderation, return that it is disabled.
        if (profile.IsDisabled)
            return new FullProfileData(user.User, content with { Description = "This profile is disabled" }, string.Empty);

        if (profile.FlaggedForReport)
            return new FullProfileData(user.User, content with { Description = "Profile is pending review from Sundouleia after being reported" }, string.Empty);

        if (profile.IsNSFW && !allowNSFW)
            return new FullProfileData(user.User, content, string.Empty);
        // If NSFW 


        return new FullProfileData(user.User, content, profile.Base64AvatarData);
    }
}

