using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;

namespace SundouleiaServer.Hubs;
#nullable enable
// A stored entity within the redis cache to serve as a helper for redis-key identification.
public record RedisLocationData(LocationMeta Location)
{
    public string? RadarId { get; set; } = null;
    public string? RadarGroupId { get; set; } = null;
    public string? RadarChatId { get; set; } = null;
}

// For radar syncing, it would be ideal to cache the current UID's of a group when they are first requested so we can easily fetch how many of them are pairs of us.
// This goes both ways, for datasyncing too.
public partial class SundouleiaHub
{
    public string RedisLocation => $"Location:{UserUID}";

    // Detatches a user from their location on abort.
    private async Task RemoveUserLocation()
    {
        if (await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false) is not { } loc)
            return;

        // Remove Chat presence
        if (loc.RadarChatId is not null)
            await _redis.HashDeleteAsync($"RadarChat:{loc.RadarChatId}", UserUID).ConfigureAwait(false);

        // Remove radar precense and inform others if so
        if (loc.RadarId is not null)
        {
            await _redis.HashDeleteAsync($"Radar:{loc.RadarId}", UserUID).ConfigureAwait(false);
            var otherUids = await _redis.HashKeysAsync($"Radar:{loc.RadarId}").ConfigureAwait(false);
            await Clients.Users(otherUids).Callback_RadarRemoveUser(new(new(UserUID))).ConfigureAwait(false);
        }

        // Remove RadarGroup precense and inform others if so
        if (loc.RadarGroupId is not null)
        {
            await _redis.HashDeleteAsync($"RadarGroup:{loc.RadarGroupId}", UserUID).ConfigureAwait(false);
            var otherUids = await _redis.HashKeysAsync($"RadarGroup:{loc.RadarGroupId}").ConfigureAwait(false);
            await Clients.Users(otherUids).Callback_RadarGroupRemoveUser(new(new(UserUID))).ConfigureAwait(false);
        }

        // Remove the location cache for the user.
        await _redis.RemoveAsync(RedisLocation).ConfigureAwait(false);
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserSendChatDM(DirectChatMessage message)
    {
        // WIP...
        return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotYetImplemented);
    }

    // Maybe detatch the User param from the LocationUpdate and do a db lookup for validity later.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<LocationUpdateResult>> UpdateLocation(LocationUpdate newLoc)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(newLoc.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<LocationUpdateResult>(SundouleiaApiEc.CanOnlyInteractWithSelf);

        // Grab the current location dto from our user, if it exists.
        var prevLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        // Construct the new locationData
        var newLocData = new RedisLocationData(newLoc.Location)
        {
            RadarId = newLoc.JoinRadar ? newLoc.Location.RadarPublicKey() : null,
            RadarGroupId = newLoc.JoinRadarGroup ? newLoc.Location.RadarGroupKey() : null,
            RadarChatId = newLoc.JoinChat ? newLoc.Location.RadarChatKey() : null
        };
        // Init the result output
        var result = new LocationUpdateResult();

        // Get if the previous locations were valid, and if we had a change in location.
        var hadOldRadar = prevLoc is not null && prevLoc.RadarId is not null;
        var hadOldRadarGroup = prevLoc is not null && prevLoc.RadarGroupId is not null;
        var hadOldChat = prevLoc is not null && prevLoc.RadarChatId is not null;

        var radarChanged = !string.Equals(prevLoc?.RadarId, newLocData.RadarId, StringComparison.Ordinal);
        var radarGroupChanged = !string.Equals(prevLoc?.RadarGroupId, newLocData.RadarGroupId, StringComparison.Ordinal);
        var chatChanged = !string.Equals(prevLoc?.RadarChatId, newLocData.RadarChatId, StringComparison.Ordinal);

        // If we were in a chat in the previous area and had an ID change, leave the old one.
        if (hadOldChat && chatChanged)
        {
            await _redis.HashDeleteAsync($"RadarChat:{prevLoc!.RadarChatId}", UserUID).ConfigureAwait(false);
            // Dont need to inform other chat users that we left (yet™)
        }

        // If our new location desired to join a chat, and it was different than before. Join it, if possible.
        if (newLoc.JoinChat && chatChanged)
        {
            await _redis.AddAsync($"RadarChat:{newLocData.RadarChatId}:{UserUID}", newLoc.ChatFlags).ConfigureAwait(false);
            result.ChatHistory = _chatService.GetRadarHistory(newLocData.RadarChatId, 50).ToList();
            // Dont need to inform other chat users that we joined (yet™)
        }

        // Collect all UIDs for batch user fetch
        var allUids = new HashSet<string>(StringComparer.Ordinal);
        IDictionary<string, ushort> radarUsers = new Dictionary<string, ushort>(StringComparer.Ordinal);
        IDictionary<string, ushort> groupUsers = new Dictionary<string, ushort>(StringComparer.Ordinal);

        // If we were in a radar before, and we changed radarIds, leave the old one.
        if (hadOldRadar && radarChanged)
        {
            await _redis.HashDeleteAsync($"Radar:{prevLoc!.RadarId}", UserUID).ConfigureAwait(false);
            var otherUids = await _redis.HashKeysAsync($"Radar:{prevLoc!.RadarId}").ConfigureAwait(false);
            await Clients.Users(otherUids).Callback_RadarRemoveUser(new(new(UserUID))).ConfigureAwait(false);
        }

        // If our new location desired to join a Radar, and it was different than before. Join it, if possible.
        // (Note: Failing to join the Radar at the new area is still considered 'safe' since it means we are in a non-radar area.
        if (newLoc.JoinRadar && radarChanged)
        {
            await _redis.AddAsync($"Radar:{newLocData.RadarId}:{UserUID}", (ushort)newLoc.PublicFlags).ConfigureAwait(false);
            radarUsers = await _redis.HashGetAllAsync<ushort>($"Radar:{newLocData.RadarId}").ConfigureAwait(false);
            radarUsers.Remove(UserUID);
            allUids.UnionWith(radarUsers.Keys);
        }

        // If we were in a RadarGroup in the previous area and had an ID change, leave the old one.
        if (hadOldRadarGroup && radarGroupChanged)
        {
            await _redis.HashDeleteAsync($"RadarGroup:{prevLoc!.RadarGroupId}", UserUID).ConfigureAwait(false);
            var otherUids = await _redis.HashKeysAsync($"RadarGroup:{prevLoc!.RadarGroupId}").ConfigureAwait(false);
            await Clients.Users(otherUids).Callback_RadarGroupRemoveUser(new(new(UserUID))).ConfigureAwait(false);
        }

        // If our new location desired to join a RadarGroup, and it was different than before. Join it, if possible.
        if (newLoc.JoinRadarGroup && radarGroupChanged)
        {
            await _redis.HashSetAsync($"RadarGroup:{newLocData.RadarGroupId}", UserUID, (ushort)newLoc.GroupFlags).ConfigureAwait(false);
            groupUsers = await _redis.HashGetAllAsync<ushort>($"RadarGroup:{newLocData.RadarGroupId}").ConfigureAwait(false);
            groupUsers.Remove(UserUID);
            allUids.UnionWith(groupUsers.Keys);
        }

        if (allUids.Count > 0)
        {
            // Perform a single query for the idents and userDatas that collect data for the union of Radar and RadarGroup
            var userDatas = await DbContext.Users.AsNoTracking().Where(u => allUids.Contains(u.UID)).ToDictionaryAsync(u => u.UID).ConfigureAwait(false);
            var idents = await GetOnlineUsers(allUids).ConfigureAwait(false);

            var radarList = new List<RadarMember>(radarUsers.Count);
            var groupList = new List<RadarGroupMember>(groupUsers.Count);

            foreach (var uid in allUids)
            {
                if (radarUsers.TryGetValue(uid, out var rFlags))
                {
                    var ident = ((RadarFlags)rFlags).HasAny(RadarFlags.ShowVisibility) ? idents.GetValueOrDefault(uid, string.Empty) : string.Empty;
                    radarList.Add(new(userDatas[uid].ToUserData(), ident, (RadarFlags)rFlags));
                }
                if (groupUsers.TryGetValue(uid, out var gFlags))
                {
                    var ident = ((RadarGroupFlags)gFlags).HasAny(RadarGroupFlags.ShowVisibility) ? idents.GetValueOrDefault(uid, string.Empty) : string.Empty;
                    groupList.Add(new(userDatas[uid].ToUserData(), ident, (RadarGroupFlags)gFlags));
                }
            }

            result.RadarUsers = radarList;
            result.RadarGroupUsers = groupList;
        }

        // Return the location shift outcome
        return HubResponseBuilder.Yippee(result);
    }

    // Returns the recent messages for the area.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<LoggedRadarChatMessage>>> RadarChatJoin(RadarChatMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.CanOnlyInteractWithSelf);

        // Ensure that this user is allowed to join radar groups at all.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(ua => ua.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.NullData);

        if (!auth.AccountRep.ChatUsage)
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.RestrictedByReputation);

        // extract our current location data
        if (await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false) is not { } curLoc)
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.BadLocationCache);

        // If the chat key exists, and we tried to join, fail as we are already present.
        if (curLoc.RadarChatId is not null)
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.AlreadyExists);

        // Attempt to create the chat key. if it returns null, we cannot join chat here.
        if (curLoc.Location.RadarChatKey() is not { } validKey)
            return HubResponseBuilder.AwDangIt<List<LoggedRadarChatMessage>>(SundouleiaApiEc.ChatForbidden);

        // By this point, we know our key is valid, the location is valid, and we are not already in a chat. So safely update and join.
        curLoc.RadarChatId = curLoc.Location.RadarChatKey();
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);

        // Join and grab all users from the chat
        await _redis.AddAsync($"RadarChat:{curLoc.RadarChatId}:{UserUID}", dto.Flags).ConfigureAwait(false);

        // Collect the recent chat and return it.
        var chatHistory = _chatService.GetRadarHistory(curLoc.RadarChatId, 50);
        return HubResponseBuilder.Yippee(chatHistory.ToList());
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarChatPermissionChange(RadarChatMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarChatId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Ensure chat presence in RadarChat hash (O(1))
        if (!await _redis.HashExistsAsync($"RadarChat:{curLoc.RadarChatId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Update permission, setting value as ushort (O(1))
        await _redis.HashSetAsync($"RadarChat:{curLoc.RadarChatId}", UserUID, (ushort)dto.Flags).ConfigureAwait(false);

        // Fetch all users in the chat hash in O(N), then remove our own UID in O(1)
        var chatUsers = await _redis.HashGetAllAsync<ushort>($"RadarChat:{curLoc.RadarChatId}").ConfigureAwait(false);
        chatUsers.Remove(UserUID);

        // Broadcast change to others.
        await Clients.Users(chatUsers.Keys).Callback_RadarChatAddUpdateUser(dto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarSendChat(SentRadarMessage dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.Sender.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarChatId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fetch all users in the chat hash in O(N)
        var chatUsers = await _redis.HashKeysAsync($"RadarChat:{curLoc.RadarChatId}").ConfigureAwait(false);

        // Log the message to the RadarChatService
        var msg = _chatService.LogRadarChat(curLoc.RadarChatId, dto);
        var loggedMsg = new LoggedRadarChatMessage(curLoc.RadarChatId, msg.MsgId, msg.TimeSentUTC, dto.Sender, dto.Message, dto.Permissions);
        // Broadcast the message to other users in the chat.
        await Clients.Users(chatUsers).Callback_RadarChatMessage(loggedMsg).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarChatLeave()
    {
        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarChatId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.BadLocationCache);

        // Get if we failed to leave or not.
        if (!await _redis.HashDeleteAsync($"RadarChat:{curLoc.RadarChatId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyLeft);

        // Update our RadarChatId to null and update cache.
        curLoc.RadarChatId = null;
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<RadarMember>>> RadarAreaJoin(RadarMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.CanOnlyInteractWithSelf);

        // Ensure that this user is allowed to join radar groups at all.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(ua => ua.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.NullData);

        if (!auth.AccountRep.RadarUsage)
            return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.RestrictedByReputation);


        // extract our current location data
        if (await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false) is not { } curLoc)
            return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.BadLocationCache);

        // Ensure that our RadarId exists. Fail otherwise.
        if (curLoc.RadarId is not null)
            return HubResponseBuilder.AwDangIt<List<RadarMember>>(SundouleiaApiEc.AlreadyExists);

        // Init the RadarId and add to cache.
        curLoc.RadarId = curLoc.Location.RadarPublicKey();
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);

        // Add ourselves to the radar set for this radarId.
        await _redis.AddAsync($"Radar:{curLoc.RadarId}:{UserUID}", (ushort)dto.Flags).ConfigureAwait(false);

        // The Ident here can intentionally be empty to assume lurker state.
        var radarUsers = await _redis.HashGetAllAsync<ushort>($"Radar:{curLoc.RadarId}").ConfigureAwait(false);
        radarUsers.Remove(UserUID);

        // Build a dictionary of user datas while also casting the flags value from radarUsers.
        var uidKeys = radarUsers.Keys.ToHashSet(StringComparer.Ordinal);
        var userDatas = await DbContext.Users.AsNoTracking()
            .Where(u => uidKeys.Contains(u.UID))
            .ToDictionaryAsync(u => u.UID, u => new { User = u, Flags = (RadarFlags)radarUsers[u.UID] })
            .ConfigureAwait(false);

        // Grab the idents in one go over each pass
        var idents = await GetOnlineUsers(radarUsers.Keys).ConfigureAwait(false);

        // Build the return result.
        var radarMembers = new List<RadarMember>();
        foreach (var (uid, data) in userDatas)
        {
            var ident = data.Flags.HasAny(RadarFlags.ShowVisibility) ? idents.GetValueOrDefault(uid, string.Empty) : string.Empty;
            radarMembers.Add(new RadarMember(data.User.ToUserData(), ident, data.Flags));
        }

        // Broadcast to other users that we joined.
        await Clients.Users(radarUsers.Keys).Callback_RadarAddUpdateUser(dto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee(radarMembers);
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarAreaPermissionChange(RadarMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Ensure chat presence in Radar hash (O(1))
        if (!await _redis.HashExistsAsync($"Radar:{curLoc.RadarId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Update permission, setting value as ushort (O(1))
        await _redis.HashSetAsync($"Radar:{curLoc.RadarId}", UserUID, (ushort)dto.Flags).ConfigureAwait(false);

        // Fetch all users in the chat hash in O(N), then remove our own UID in O(1)
        var radarUsers = await _redis.HashGetAllAsync<ushort>($"Radar:{curLoc.RadarId}").ConfigureAwait(false);
        radarUsers.Remove(UserUID);

        // (Can ensure the Ident is or isn't set based on if what its flag is, but dont worry about that now)
        await Clients.Users(radarUsers.Keys).Callback_RadarAddUpdateUser(dto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarAreaLeave()
    {
        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarChatId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.BadLocationCache);

        // Get if we failed to leave or not.
        if (!await _redis.HashDeleteAsync($"Radar:{curLoc.RadarId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyLeft);

        // Grab the other users left in the radar
        var otherUids = await _redis.HashKeysAsync($"Radar:{curLoc.RadarId}").ConfigureAwait(false);
        await Clients.Users(otherUids).Callback_RadarRemoveUser(new(new(UserUID))).ConfigureAwait(false);

        // Update our RadarId to null and update cache.
        curLoc.RadarId = null;
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<RadarGroupMember>>> RadarGroupJoin(RadarGroupMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.CanOnlyInteractWithSelf);

        // Ensure that this user is allowed to join radar groups at all.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(ua => ua.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.NullData);

        if (!auth.AccountRep.RadarUsage)
            return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.RestrictedByReputation);

        // extract our current location data
        if (await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false) is not { } curLoc)
            return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.BadLocationCache);

        // Ensure that our RadarGroupId exists. Fail otherwise.
        if (curLoc.RadarGroupId is not null)
            return HubResponseBuilder.AwDangIt<List<RadarGroupMember>>(SundouleiaApiEc.AlreadyExists);

        // Init the RadarGroupId and add to cache.
        curLoc.RadarGroupId = curLoc.Location.RadarGroupKey();
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);

        // Add ourselves to the radar group hash for this RadarGroupId.
        await _redis.HashSetAsync($"RadarGroup:{curLoc.RadarGroupId}", UserUID, (ushort)dto.Flags).ConfigureAwait(false);

        // The Ident here can intentionally be empty to assume lurker state.
        var groupUsers = await _redis.HashGetAllAsync<ushort>($"RadarGroup:{curLoc.RadarGroupId}").ConfigureAwait(false);
        groupUsers.Remove(UserUID);

        // Build a dictionary of user datas while also casting the flags value from groupUsers.
        var uidKeys = groupUsers.Keys.ToHashSet(StringComparer.Ordinal);
        var userDatas = await DbContext.Users.AsNoTracking()
            .Where(u => uidKeys.Contains(u.UID))
            .ToDictionaryAsync(u => u.UID, u => new { User = u, Flags = (RadarGroupFlags)groupUsers[u.UID] })
            .ConfigureAwait(false);

        // Grab the idents in one go
        var idents = await GetOnlineUsers(groupUsers.Keys).ConfigureAwait(false);

        // Build the return result.
        var groupMembers = new List<RadarGroupMember>();
        foreach (var (uid, data) in userDatas)
        {
            var ident = data.Flags.HasAny(RadarGroupFlags.ShowVisibility) ? idents.GetValueOrDefault(uid, string.Empty) : string.Empty;
            groupMembers.Add(new RadarGroupMember(data.User.ToUserData(), ident, data.Flags));
        }

        // Broadcast to other users that we joined.
        await Clients.Users(uidKeys).Callback_RadarGroupAddUpdateUser(dto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee(groupMembers);


    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarGroupPermissionChange(RadarGroupMember dto)
    {
        // Validate user (can use database if people try bypassing this easy check)
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarGroupId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Ensure presence in RadarGroup hash (O(1))
        if (!await _redis.HashExistsAsync($"RadarGroup:{curLoc.RadarGroupId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Update permission, setting value as ushort (O(1))
        await _redis.HashSetAsync($"RadarGroup:{curLoc.RadarGroupId}", UserUID, (ushort)dto.Flags).ConfigureAwait(false);

        // Fetch all users in the RadarGroup hash in O(N), then remove our own UID in O(1)
        var groupUsers = await _redis.HashGetAllAsync<ushort>($"RadarGroup:{curLoc.RadarGroupId}").ConfigureAwait(false);
        groupUsers.Remove(UserUID);

        // (Can ensure the Ident is or isn't set based on if what its flag is, but dont worry about that now)
        await Clients.Users(groupUsers.Keys).Callback_RadarGroupAddUpdateUser(dto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarGroupLeave()
    {
        // extract our current location data
        var curLoc = await _redis.GetAsync<RedisLocationData>(RedisLocation).ConfigureAwait(false);
        if (curLoc is null || curLoc.RadarGroupId is null)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.BadLocationCache);

        // Get if we failed to leave or not.
        if (!await _redis.HashDeleteAsync($"RadarGroup:{curLoc.RadarGroupId}", UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyLeft);

        // Grab the other users left in the RadarGroup
        var otherUids = await _redis.HashKeysAsync($"RadarGroup:{curLoc.RadarGroupId}").ConfigureAwait(false);
        await Clients.Users(otherUids).Callback_RadarGroupRemoveUser(new(new(UserUID))).ConfigureAwait(false);

        // Update our RadarGroupId to null and update cache.
        curLoc.RadarGroupId = null;
        await _redis.AddAsync(RedisLocation, curLoc).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }
}
#nullable disable

