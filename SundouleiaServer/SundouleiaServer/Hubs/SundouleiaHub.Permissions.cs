using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaAPI.Util;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;

namespace SundouleiaServer.Hubs;
#nullable enable

public partial class SundouleiaHub
{
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeGlobalsSingle(SingleChangeGlobal dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Cannot update anyone but self.
        if (!string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CanOnlyInteractWithSelf);

        // Prevent updates if the global permissions are not present.
        if (await DbContext.UserGlobalPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID).ConfigureAwait(false) is not { } globals)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Prevent processing updates that could not be set.
        if (!PropertyChanger.TrySetProperty(globals, dto.PermName, dto.NewValue, out object? convertedValue))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);

        // permission updated properly, update and save DB.
        DbContext.Update(globals);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Locate all online pairs of the caller, and send them that the permission updated.
        var pairsOfCaller = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var onlinePairsOfCaller = await GetOnlineUsers(pairsOfCaller).ConfigureAwait(false);
        IEnumerable<string> onlineUids = onlinePairsOfCaller.Keys;

        await Clients.Users(onlineUids).Callback_SingleChangeGlobal(new(dto.User, dto.PermName, dto.NewValue)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateGlobal);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeGlobalsBulk(GlobalPerms dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent updates if the global permissions are not present.
        if (await DbContext.UserGlobalPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID).ConfigureAwait(false) is not { } globals)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Update the permissions to the new values.
        dto.UpdateDbModel(globals);
        DbContext.Update(globals);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Locate all online pairs of the caller, and send them that the permission updated.
        var pairsOfCaller = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var onlinePairsOfCaller = await GetOnlineUsers(pairsOfCaller).ConfigureAwait(false);
        IEnumerable<string> onlineUids = onlinePairsOfCaller.Keys;

        await Clients.Users(onlineUids).Callback_BulkChangeGlobal(new(new(UserUID), dto)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateGlobal);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeUniqueSingle(SingleChangeUnique dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent calls for self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Prevent updates if entry does not exist.
        if (await DbContext.ClientPairPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID && u.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } ownPerms)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);


        bool prevPauseState = ownPerms.PauseVisuals;

        // Ensure change is valid.
        if (!PropertyChanger.TrySetProperty(ownPerms, dto.PermName, dto.NewValue, out object? convertedValue))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);

        // Update and save the Db.
        DbContext.Update(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Send back to the other user that the callers permission updated.
        await Clients.User(dto.User.UID).Callback_SingleChangeUnique(new(new(UserUID), dto.PermName, dto.NewValue)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique);

        // If the permission for pauseVisuals is not what it was prior to the change, we had a pause change.
        var pauseWasToggled = ownPerms.PauseVisuals != prevPauseState;

        // If it was not, we are done here and can return.
        if (!(ownPerms.PauseVisuals != prevPauseState))
            return HubResponseBuilder.Yippee();

        // Check to see if the pair we changed our permissions for also has us paused or not. If they do, just return.
        var otherPerms = await DbContext.ClientPairPerms.AsNoTracking().SingleAsync(u => u.UserUID == dto.User.UID && u.OtherUserUID == UserUID).ConfigureAwait(false);
        if (otherPerms.PauseVisuals)
            return HubResponseBuilder.Yippee();

        // They do not have us paused, so we need to send them a offline/online message based on the toggle type. (if they are online)
        if (await GetUserIdent(dto.User.UID).ConfigureAwait(false) is { } otherIdent && UserCharaIdent is not null)
        {
            if (ownPerms.PauseVisuals)
            {
                await Clients.User(UserUID).Callback_UserOffline(new(new(dto.User.UID))).ConfigureAwait(false);
                await Clients.User(dto.User.UID).Callback_UserOffline(new(new(UserUID))).ConfigureAwait(false);
            }
            else
            {
                await Clients.User(UserUID).Callback_UserOnline(new(new(dto.User.UID), otherIdent)).ConfigureAwait(false);
                await Clients.User(dto.User.UID).Callback_UserOnline(new(new(UserUID), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeUniqueBulk(BulkChangeUnique dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent changing unique permissions for a target of self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Prevent changing permissions when not paired.
        if (await DbContext.ClientPairPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID && u.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } ownPerms)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        // remember previous pause state.
        var prevPauseState = ownPerms.PauseVisuals;

        // Set, Update, & Save.
        dto.NewPerms.UpdateDbModel(ownPerms);
        DbContext.Update(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Notify the pair of success.
        // Send back to the other user that the callers permission updated.
        await Clients.User(dto.User.UID).Callback_BulkChangeUnique(new(new(UserUID), dto.NewPerms)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique);

        // If the permission for pauseVisuals is not what it was prior to the change, we had a pause change.
        var pauseWasToggled = ownPerms.PauseVisuals != prevPauseState;

        // If it was not, we are done here and can return.
        if (!(ownPerms.PauseVisuals != prevPauseState))
            return HubResponseBuilder.Yippee();

        // Check to see if the pair we changed our permissions for also has us paused or not. If they do, just return.
        var otherPerms = await DbContext.ClientPairPerms.AsNoTracking().SingleAsync(u => u.UserUID == dto.User.UID && u.OtherUserUID == UserUID).ConfigureAwait(false);
        if (otherPerms.PauseVisuals)
            return HubResponseBuilder.Yippee();

        // They do not have us paused, so we need to send them a offline/online message based on the toggle type. (if they are online)
        if (await GetUserIdent(dto.User.UID).ConfigureAwait(false) is { } otherIdent && UserCharaIdent is not null)
        {
            if (ownPerms.PauseVisuals)
            {
                await Clients.User(UserUID).Callback_UserOffline(new(new(dto.User.UID))).ConfigureAwait(false);
                await Clients.User(dto.User.UID).Callback_UserOffline(new(new(UserUID))).ConfigureAwait(false);
            }
            else
            {
                await Clients.User(UserUID).Callback_UserOnline(new(new(dto.User.UID), otherIdent)).ConfigureAwait(false);
                await Clients.User(dto.User.UID).Callback_UserOnline(new(new(UserUID), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        return HubResponseBuilder.Yippee();
    }
}

