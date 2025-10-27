using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Services;
using SundouleiaServer.Utils;

namespace SundouleiaServer.Hubs;

public partial class SundouleiaHub
{
    /// <summary>
    ///     Fired upon the client caller joining a zone, or enabling their radar.
    /// </summary>
    /// <returns> The list of active radar users in the same zone as the caller. </returns>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<RadarZoneInfo>> RadarZoneJoin(RadarZoneUpdate dto)
    {
        // Fail if the zone data does not exist.
        if (await DbContext.UserRadarInfo.Include(u => u.User).SingleOrDefaultAsync(ur => ur.UserUID == UserUID).ConfigureAwait(false) is not { } info)
            return HubResponseBuilder.AwDangIt<RadarZoneInfo>(SundouleiaApiEc.NullData);

        // Fail if no auth exists for the caller.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(ua => ua.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return HubResponseBuilder.AwDangIt<RadarZoneInfo>(SundouleiaApiEc.NullData);

        // If the AccountRep prevents joining Radars, do not join them.
        if (!auth.AccountRep.RadarUsage)
            return HubResponseBuilder.AwDangIt<RadarZoneInfo>(SundouleiaApiEc.RestrictedByReputation);

        _logger.LogMessage($"UserJoinZone:[{UserUID}][{dto.WorldId}][{dto.TerritoryId}]");

        // Collect the groups of individuals in the same area as you.
        var onlineUsersInArea = await DbContext.UserRadarInfo.Include(r => r.User)
            .AsNoTracking()
            .Where(r => r.WorldId == dto.WorldId && r.TerritoryId == dto.TerritoryId)
            .Select(r => new OnlineUser(r.User.ToUserData(), r.HashedCID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Update the world and zone info regardless of what the other settings are.
        info.WorldId = dto.WorldId;
        info.TerritoryId = dto.TerritoryId;
        info.HashedCID = dto.HashedCID;
        DbContext.Update(info);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform the other users in the area that you joined.
        var radarUids = onlineUsersInArea.Select(u => u.User.UID);
        await Clients.Users(radarUids).Callback_RadarAddUpdateUser(new(info.User.ToUserData(), info.HashedCID)).ConfigureAwait(false);

        // if we wanted to join the chat room, do so. (They must also have appropriate reputation.
        if (dto.JoinChat && auth.AccountRep.ChatUsage)
        {
            // If the count exceeds the cap, do not join the group.
            var count = _radarService.GetActiveGroupUsers(dto.WorldId, dto.TerritoryId);
            if (count >= RadarService.RadarChatCap)
                await Clients.Caller.Callback_ServerMessage(MessageSeverity.Warning, "The chat for this radar zone is capped. Cannot join!").ConfigureAwait(false);
            else
                await _radarService.JoinRadarChat(dto.WorldId, dto.TerritoryId, Context.ConnectionId).ConfigureAwait(false);
        }

        // ret the zoneInfo.
        return HubResponseBuilder.Yippee<RadarZoneInfo>(new(onlineUsersInArea));
    }

    /// <summary>
    ///     Usually called upon the client caller leaving a zone or disabling their radar.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarZoneLeave()
    {
        // Fail if the zone data does not exist.
        if (await DbContext.UserRadarInfo.SingleOrDefaultAsync(ur => ur.UserUID == UserUID).ConfigureAwait(false) is not { } info)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Remember the area we left.
        var leftWorld = info.WorldId;
        var leftTerritory = info.TerritoryId;

        // Reset the values.
        info.WorldId = ushort.MaxValue;
        info.TerritoryId = ushort.MaxValue;
        info.HashedCID = string.Empty;
        DbContext.Update(info);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform the other users in the area that you left.
        var otherUserUidsInArea = await DbContext.UserRadarInfo.AsNoTracking()
            .Where(r => r.WorldId == leftWorld && r.TerritoryId == leftTerritory)
            .Select(r => r.UserUID)
            .ToListAsync()
            .ConfigureAwait(false);
        await Clients.Users(otherUserUidsInArea).Callback_RadarRemoveUser(new(new(UserUID))).ConfigureAwait(false);

        // If we were in any chat group. Leave it.
        if (_radarService.IsInChat(Context.ConnectionId))
            await _radarService.LeaveRadarChat(Context.ConnectionId).ConfigureAwait(false);

        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     Whenever the caller updates their radar preferences, this is called to
    ///     ensure that the current state in the DB is updated accordingly.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarUpdateState(RadarState stateUpdate)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(stateUpdate));

        // Fail if the zone data does not exist.
        if (await DbContext.UserRadarInfo.SingleOrDefaultAsync(ur => ur.UserUID == UserUID).ConfigureAwait(false) is not { } info)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Prevent updates if we have not joined a zone.
        if (info.WorldId == ushort.MaxValue && info.TerritoryId == ushort.MaxValue)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotInZone);

        // Fail if no auth exists for the caller.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(ua => ua.UserUID == UserUID).ConfigureAwait(false) is not { } auth)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // If the CID is different, update it, and also update the db and save changes.
        if (!string.Equals(info.HashedCID, stateUpdate.HashedCID, StringComparison.Ordinal))
        {
            info.HashedCID = stateUpdate.HashedCID;
            DbContext.Update(info);
            await DbContext.SaveChangesAsync().ConfigureAwait(false);
            // Update all other users in the area of the change.
            var uidsToUpdate = await DbContext.UserRadarInfo.AsNoTracking()
                .Where(r => r.WorldId == info.WorldId && r.TerritoryId == info.TerritoryId)
                .Select(r => r.UserUID)
                .ToListAsync()
                .ConfigureAwait(false);
            await Clients.Users(uidsToUpdate).Callback_RadarAddUpdateUser(new(new(UserUID), info.HashedCID)).ConfigureAwait(false);
        }

        // Handle ChatRoom state.
        var inChatRoom = _radarService.IsInChat(Context.ConnectionId);

        // Leaving Radar Chat
        if (inChatRoom && !stateUpdate.UseChat)
            await _radarService.LeaveRadarChat(Context.ConnectionId).ConfigureAwait(false);
        // Joining Radar Chat (Only allow if not capped & allowed)
        else if (!inChatRoom && stateUpdate.UseChat && auth.AccountRep.ChatUsage)
        {
            if (_radarService.GetActiveGroupUsers(info.WorldId, info.TerritoryId) >= RadarService.RadarChatCap)
                await Clients.Caller.Callback_ServerMessage(MessageSeverity.Warning, "The chat for this radar zone is capped. Cannot join!").ConfigureAwait(false);
            else
                await _radarService.JoinRadarChat(info.WorldId, info.TerritoryId, Context.ConnectionId).ConfigureAwait(false);
        }

        return HubResponseBuilder.Yippee();
    }

    // Should never be able to send a radar chat message if not in a zone, so no security checks needed for this.
    // If people down the line are getting freaky and sending fake chat messages for reports,
    // we can always cache the messages in a circular buffer for each zone.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RadarChatMessage(RadarChatMessage chatDto)
    {
        // Grab the group this message is for. (We will do it this way to start. If people are being
        // annoying and bypassing this we will just check the zone info entry.
        var groupName = _radarService.GetGroupName(chatDto.WorldId, chatDto.TerritoryId);
        await Clients.Group(groupName).Callback_RadarChat(chatDto).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }
}

