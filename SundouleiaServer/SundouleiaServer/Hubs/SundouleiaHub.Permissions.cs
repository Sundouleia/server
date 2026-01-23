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
    public async Task<HubResponse> UserChangeGlobalsSingle(ChangeGlobalPerm dto)
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

        await Clients.Users(onlineUids).Callback_ChangeGlobalPerm(new(dto.User, dto.PermName, dto.NewValue)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateGlobal);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeAllGlobals(GlobalPerms dto)
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

        await Clients.Users(onlineUids).Callback_ChangeAllGlobal(new(new(UserUID), dto)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateGlobal);
        return HubResponseBuilder.Yippee();
    }


    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserChangeUniquePerm(ChangeUniquePerm dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent calls for self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Prevent updates if entry does not exist.
        if (await DbContext.ClientPairPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID && u.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } ownPerms)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        bool prevPauseState = ownPerms.PauseVisuals;

        // Ensure change is valid.
        if (!PropertyChanger.TrySetProperty(ownPerms, dto.PermName, dto.NewValue, out object? convertedValue))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);

        // Update and save the Db.
        DbContext.Update(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Send back to the other user that the callers permission updated.
        await Clients.User(dto.User.UID).Callback_ChangeUniquePerm(new(new(UserUID), dto.PermName, dto.NewValue)).ConfigureAwait(false);
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
    public async Task<HubResponse> UserChangeUniquePerms(ChangeUniquePerms dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent calls for self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Prevent updates if entry does not exist.
        if (await DbContext.ClientPairPerms.SingleOrDefaultAsync(u => u.UserUID == UserUID && u.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } ownPerms)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        bool prevPauseState = ownPerms.PauseVisuals;

        // Ensure changes are valid. If any fail, return before updating and saving the changes.
        foreach (var (perm, newValue) in dto.Changes)
            if (!PropertyChanger.TrySetProperty(ownPerms, perm, newValue, out object? _))
                return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);

        // Update and save the Db.
        DbContext.Update(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Send back to the other user that the callers permission updated.
        await Clients.User(dto.User.UID).Callback_ChangeUniquePerms(new(new(UserUID), dto.Changes)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique, dto.Changes.Count);

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
    public async Task<HubResponse> UserChangeAllUnique(ChangeAllUnique dto)
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
        await Clients.User(dto.User.UID).Callback_ChangeAllUnique(new(new(UserUID), dto.NewPerms)).ConfigureAwait(false);
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

    // The below methods could be potentially optimized further, but they are fine as is for now.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserBulkChangeUniquePerm(BulkChangeUniquePerm dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // Convert to hashset for O(1) lookups.
        var targetUids = dto.Users.Select(u => u.UID).ToHashSet(StringComparer.Ordinal);
        if (targetUids.Contains(UserUID))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Fetch all ClientPairPerms entries for the caller that match the target UIDs.
        var ownPerms = await DbContext.ClientPairPerms
            .Where(u => u.UserUID == UserUID && targetUids.Contains(u.OtherUserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Ensure we have valid entries.
        if (ownPerms.Count != targetUids.Count)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        // Convert to dictionary.
        var ownPermsByUid = ownPerms.ToDictionary(p => p.OtherUserUID, StringComparer.Ordinal);

        // Track pause state changes
        var pauseChangedUids = new List<string>(ownPerms.Count);

        // Apply the property updates.
        foreach (var perm in ownPerms)
        {
            var prevPause = perm.PauseVisuals;

            if (!PropertyChanger.TrySetProperty(perm, dto.PermName, dto.NewValue, out object? _))
                return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);

            if (perm.PauseVisuals != prevPause)
                pauseChangedUids.Add(perm.OtherUserUID);
        }

        // Save all changes to the Db.
        DbContext.UpdateRange(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Update Metrics.
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique);

        // Inform all online users of the update.
        await Clients.Users(ownPermsByUid.Keys).Callback_ChangeUniquePerm(new(new(UserUID), dto.PermName, dto.NewValue)).ConfigureAwait(false);

        // If paused was not touched, complete.
        if (pauseChangedUids.Count == 0 || UserCharaIdent is null)
            return HubResponseBuilder.Yippee();

        // Get the current online users via redis, from the pausechanged uids.
        var onlineUsers = await GetOnlineUsers(pauseChangedUids).ConfigureAwait(false);

        // Identify which of the clientPairPerms match this subset.
        var otherPermsList = await DbContext.ClientPairPerms
            .AsNoTracking()
            .Where(u => u.OtherUserUID == UserUID && onlineUsers.ContainsKey(u.UserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Perform the online / offline notifications.
        foreach (var otherPerms in otherPermsList)
        {
            // if their pauseVisuals was true, skip.
            if (otherPerms.PauseVisuals)
                continue;

            // Otherwise, inform them based on what our pause state was.
            if (ownPermsByUid[otherPerms.UserUID].PauseVisuals)
            {
                await Clients.User(UserUID).Callback_UserOffline(new(new(otherPerms.UserUID))).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOffline(new(new(UserUID))).ConfigureAwait(false);
            }
            else
            {
                await Clients.User(UserUID).Callback_UserOnline(new(new(otherPerms.UserUID), onlineUsers[otherPerms.UserUID])).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOnline(new(new(UserUID), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserBulkChangeUniquePerms(BulkChangeUniquePerms dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // Convert to hashset for O(1) lookups.
        var targetUids = dto.Users.Select(u => u.UID).ToHashSet(StringComparer.Ordinal);
        if (targetUids.Contains(UserUID))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Fetch all ClientPairPerms entries for the caller that match the target UIDs.
        var ownPerms = await DbContext.ClientPairPerms
            .Where(u => u.UserUID == UserUID && targetUids.Contains(u.OtherUserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Ensure we have valid entries.
        if (ownPerms.Count != targetUids.Count)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        var ownPermsByUid = ownPerms.ToDictionary(p => p.OtherUserUID, StringComparer.Ordinal);
        var changedPause = dto.Changes.ContainsKey(nameof(PairPerms.PauseVisuals));

        // Apply the property updates.
        foreach (var perm in ownPerms)
        {
            foreach (var (permName, newValue) in dto.Changes)
                if (!PropertyChanger.TrySetProperty(perm, permName, newValue, out object? _))
                    return HubResponseBuilder.AwDangIt(SundouleiaApiEc.IncorrectDataType);
        }

        // Save all changes to the Db.
        DbContext.UpdateRange(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Update Metrics.
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique, ownPerms.Count);

        // Inform all online users of the update.
        await Clients.Users(ownPermsByUid.Keys).Callback_ChangeUniquePerms(new(new(UserUID), dto.Changes)).ConfigureAwait(false);

        // If paused was not touched, complete.
        if (!changedPause || UserCharaIdent is null)
            return HubResponseBuilder.Yippee();

        // Get the current online users via redis, from the pausechanged uids.
        var onlineUsers = await GetOnlineUsers(ownPermsByUid.Keys.ToList()).ConfigureAwait(false);

        // Identify which of the clientPairPerms match this subset.
        var otherPermsList = await DbContext.ClientPairPerms
            .AsNoTracking()
            .Where(u => u.OtherUserUID == UserUID && onlineUsers.ContainsKey(u.UserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Perform the online / offline notifications.
        foreach (var otherPerms in otherPermsList)
        {
            // if their pauseVisuals was true, skip.
            if (otherPerms.PauseVisuals)
                continue;

            // Otherwise, inform them based on what our pause state was.
            if (ownPermsByUid[otherPerms.UserUID].PauseVisuals)
            {
                await Clients.User(UserUID).Callback_UserOffline(new(new(otherPerms.UserUID))).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOffline(new(new(UserUID))).ConfigureAwait(false);
            }
            else
            {
                await Clients.User(UserUID).Callback_UserOnline(new(new(otherPerms.UserUID), onlineUsers[otherPerms.UserUID])).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOnline(new(new(UserUID), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        return HubResponseBuilder.Yippee();
    }

    public async Task<HubResponse> UserBulkChangeAllUnique(BulkChangeAllUnique dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // Convert to hashset for O(1) lookups.
        var targetUids = dto.Users.Select(u => u.UID).ToHashSet(StringComparer.Ordinal);
        if (targetUids.Contains(UserUID))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Fetch all ClientPairPerms entries for the caller that match the target UIDs.
        var ownPerms = await DbContext.ClientPairPerms
            .Where(u => u.UserUID == UserUID && targetUids.Contains(u.OtherUserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Ensure we have valid entries.
        if (ownPerms.Count != targetUids.Count)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        var ownPermsByUid = ownPerms.ToDictionary(p => p.OtherUserUID, StringComparer.Ordinal);
        var changedPause = false;

        // Apply the property updates.
        foreach (var ownPerm in ownPerms)
        {
            var prevPause = ownPerm.PauseVisuals;
            dto.NewPerms.UpdateDbModel(ownPerm);
            // correctly track if pause was changed.
            changedPause |= ownPerm.PauseVisuals != prevPause;
        }

        // Save all changes to the Db.
        DbContext.UpdateRange(ownPerms);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Update Metrics.
        _metrics.IncCounter(MetricsAPI.CounterPermissionUpdateUnique, ownPerms.Count);

        // Inform all online users of the update.
        await Clients.Users(ownPermsByUid.Keys).Callback_ChangeAllUnique(new(new(UserUID), dto.NewPerms)).ConfigureAwait(false);

        // If paused was not touched, complete.
        if (!changedPause || UserCharaIdent is null)
            return HubResponseBuilder.Yippee();

        // Get the current online users via redis, from the pausechanged uids.
        var onlineUsers = await GetOnlineUsers(ownPermsByUid.Keys.ToList()).ConfigureAwait(false);

        // Identify which of the clientPairPerms match this subset.
        var otherPermsList = await DbContext.ClientPairPerms
            .AsNoTracking()
            .Where(u => u.OtherUserUID == UserUID && onlineUsers.ContainsKey(u.UserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // Perform the online / offline notifications.
        foreach (var otherPerms in otherPermsList)
        {
            // if their pauseVisuals was true, skip.
            if (otherPerms.PauseVisuals)
                continue;

            // Otherwise, inform them based on what our pause state was.
            if (ownPermsByUid[otherPerms.UserUID].PauseVisuals)
            {
                await Clients.User(UserUID).Callback_UserOffline(new(new(otherPerms.UserUID))).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOffline(new(new(UserUID))).ConfigureAwait(false);
            }
            else
            {
                await Clients.User(UserUID).Callback_UserOnline(new(new(otherPerms.UserUID), onlineUsers[otherPerms.UserUID])).ConfigureAwait(false);
                await Clients.User(otherPerms.UserUID).Callback_UserOnline(new(new(UserUID), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        return HubResponseBuilder.Yippee();
    }
}

