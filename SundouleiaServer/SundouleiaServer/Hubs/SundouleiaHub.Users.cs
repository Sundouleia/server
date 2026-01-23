using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using SundouleiaAPI.Data;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;
using System.Reflection;

namespace SundouleiaServer.Hubs;
#nullable enable

public partial class SundouleiaHub
{
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<SundesmoRequest>> UserSendRequest(CreateRequest dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // The target to send the request to.
        string uid = dto.User.UID.Trim();

        // Prevent sending requests to self.
        if (string.Equals(uid, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.InvalidRecipient);

        // Prevent sending to a user not registered in Sundouleia.
        if (await DbContext.Users.SingleOrDefaultAsync(u => u.UID == uid).ConfigureAwait(false) is not { } target)
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Warning, $"Cannot send Request to {dto.User.UID}, they don't exist").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.InvalidRecipient);
        }

        // Sort the following calls by estimated tables with least entries to most entries for efficiency.

        // Prevent sending a request if either end has blocked the other.
        if (await DbContext.BlockedUsers.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.RecipientBlocked);

        // Ensure that the request does not already exist in the database.
        if (await DbContext.Requests.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.AlreadyExists);

        // Prevent sending a request if already paired.
        if (await DbContext.ClientPairs.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.AlreadyPaired);

        // Request is valid for sending, so retrieve the context callers user model.
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        // Create and add the request to the DB, and save changes.
        var newRequest = new PairRequest()
        {
            User = callerUser,
            OtherUser = target,
            IsTemporary = dto.IsTemp,
            AttachedMessage = dto.Message,
            CreationTime = DateTime.UtcNow,
        };
        await DbContext.Requests.AddAsync(newRequest).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Need to make a DTO version of this that is sent back.
        var callbackDto = newRequest.ToApi();

        // If the target user's UID is in the redis DB, send them the pending request.
        if (await GetUserIdent(uid).ConfigureAwait(false) is not null)
            await Clients.User(uid).Callback_AddRequest(callbackDto).ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterRequestsCreated);
        _metrics.IncGauge(MetricsAPI.GaugeRequestsPending);
        return HubResponseBuilder.Yippee(callbackDto);
    }

    /// <summary>
    ///     If either the creator of a of the request cancels the request prior to its expiration time. <para />
    ///     Monitor this callback, if it returns successful, the caller should remove the request they wished to cancel.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserCancelRequest(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // The target to send the request to.
        string uid = dto.User.UID.Trim();

        // Prevent sending requests to self.
        if (string.Equals(uid, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Prevent sending to a user not registered in Sundouleia.
        if (await DbContext.Users.SingleOrDefaultAsync(u => u.UID == uid).ConfigureAwait(false) is not { } target)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Ensure that the request does not already exist in the database.
        if (await DbContext.Requests.SingleOrDefaultAsync(k => k.UserUID == UserUID && k.OtherUserUID == target.UID).ConfigureAwait(false) is not { } request)
            return HubResponseBuilder.AwDangIt<SundesmoRequest>(SundouleiaApiEc.AlreadyExists);

        // Can cancel the request:
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        // Create the dummy callback for removal.
        var callbackDto = Extensions.ToApiRemoval(new(UserUID), new(uid));
        
        // send off to the other user if they are online.
        if (await GetUserIdent(uid).ConfigureAwait(false) is { } otherIdent) 
            await Clients.User(uid).Callback_RemoveRequest(callbackDto).ConfigureAwait(false);

        // remove request from db and return.
        DbContext.Requests.Remove(request);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
         
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserCancelRequests(UserListDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        var targetUids = dto.Users.Select(u => u.UID.Trim()).ToList();

        // Fetch all request information in bulk using psql query language.
        var dbRequests = await GetSentRequests(targetUids).ConfigureAwait(false);

        // If none exist, return null data.
        if (dbRequests.Count == 0)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Prepare the callback DTO's for our callback notifications.
        var callbacks = dbRequests
            .Select(kvp => Extensions.ToApiRemoval(new(UserUID), new(kvp.Key)))
            .ToList();

        // Notify the online users.
        foreach (var callback in callbacks)
        {
            if (await GetUserIdent(callback.Target.UID).ConfigureAwait(false) is not null)
                await Clients.User(callback.Target.UID).Callback_RemoveRequest(callback).ConfigureAwait(false);
        }

        // Remove all requests in bulk.
        DbContext.Requests.RemoveRange(dbRequests.Values);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending, dbRequests.Count);
        return HubResponseBuilder.Yippee();
    }


    /// <summary> 
    ///     Triggered whenever the recipient of a request accepts it. <para />
    ///     Bare in mind that due to the way this is called, the person accepting is the request entry <b>TARGET</b>. <para />
    ///     <b>If caller receives "AlreadyPaired", they should remove the request from their list.</b>
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<AddedUserPair>> UserAcceptRequest(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        var uid = dto.User.UID.Trim();

        // Prevent accepting a request for self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(dto.User.UID))
            return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.CannotInteractWithSelf);

        // Prevent sending to a user not registered in Sundouleia.
        if (await DbContext.Users.SingleOrDefaultAsync(u => u.UID == uid).ConfigureAwait(false) is not { } target)
            return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.NullData);

        // Ensure that the request does not already exist in the database.
        if (await DbContext.Requests.SingleOrDefaultAsync(k => k.UserUID == target.UID && k.OtherUserUID == UserUID).ConfigureAwait(false) is not { } request)
            return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.RequestNotFound);

        var wasTempRequest = request.IsTemporary;

        // Must not be already paired. If you are, discard the request regardless, but return with error. 
        if (await DbContext.ClientPairs.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
        {
            DbContext.Requests.Remove(request);
            await DbContext.SaveChangesAsync().ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.AlreadyPaired);
        }

        // Request is properly accepted, create the relationship pair.
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        var callerToRecipient = new ClientPair()
        {
            User = callerUser,
            OtherUser = target,
            CreatedAt = DateTime.UtcNow,
            TempAccepterUID = wasTempRequest ? callerUser.UID : string.Empty,
        };
        var recipientToCaller = new ClientPair()
        {
            User = target,
            OtherUser = callerUser,
            CreatedAt = DateTime.UtcNow,
            TempAccepterUID = wasTempRequest ? callerUser.UID : string.Empty,
        };
        // Add them to the DB.
        await DbContext.ClientPairs.AddAsync(callerToRecipient).ConfigureAwait(false);
        await DbContext.ClientPairs.AddAsync(recipientToCaller).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // If the existing data is marked as null then we should abort as there is a serious issue going on.
        // This should only return null if they are not paired, but we just added that they are.
        var existingData = await GetPairInfo(UserUID, target.UID).ConfigureAwait(false);

        // We can reliably assume that everything but the pair permissions are valid at this point.
        var newOwnPerms = existingData?.OwnPerms;
        if (newOwnPerms is null)
        {
            newOwnPerms = new ClientPairPermissions()
            {
                User = callerUser,
                OtherUser = target,
                PauseVisuals = false,
                AllowAnimations = existingData!.OwnGlobals.DefaultAllowAnimations,
                AllowSounds = existingData!.OwnGlobals.DefaultAllowSounds,
                AllowVfx = existingData!.OwnGlobals.DefaultAllowVfx,
                MoodleAccess = existingData!.OwnGlobals.DefaultMoodleAccess,
                MaxMoodleTime = TimeSpan.Zero,
                ShareOwnMoodles = existingData!.OwnGlobals.DefaultShareOwnMoodles,
            };
            await DbContext.ClientPairPerms.AddAsync(newOwnPerms).ConfigureAwait(false);
        }

        var newOtherPerms = existingData?.OtherPerms;
        if (newOtherPerms is null)
        {
            newOtherPerms = new ClientPairPermissions()
            {
                User = target,
                OtherUser = callerUser,
                PauseVisuals = false,
                AllowAnimations = existingData!.OtherGlobals.DefaultAllowAnimations,
                AllowSounds = existingData!.OtherGlobals.DefaultAllowSounds,
                AllowVfx = existingData!.OtherGlobals.DefaultAllowVfx,
                MoodleAccess = existingData!.OtherGlobals.DefaultMoodleAccess,
                MaxMoodleTime = TimeSpan.Zero,
                ShareOwnMoodles = existingData!.OtherGlobals.DefaultShareOwnMoodles,
            };
            await DbContext.ClientPairPerms.AddAsync(newOtherPerms).ConfigureAwait(false);
        }

        // Remove the request and save the changes.
        DbContext.Requests.Remove(request);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        
        // Compile together the UserPair we will return to the caller.
        // This is the UserPair of the Caller (Request Accepter) -> Target (Request Creator)
        var callerRetDto = new UserPair(
            target.ToUserData(), 
            newOwnPerms.ToApi(), 
            existingData!.OtherGlobals.ToApi(),
            newOtherPerms.ToApi(),
            existingData.PairInitAt,
            existingData.PairTempAccepter
        );

        // Get if the request creator is online of not.
        var requesterIdentity = await GetUserIdent(uid).ConfigureAwait(false);

        // If the request creator is online, send them the remove request and add pair callbacks, and also return sendOnline to both.
        if (requesterIdentity is not null)
        {
            var requesterRetDto = new UserPair(
                callerUser.ToUserData(),
                newOtherPerms.ToApi(),
                existingData!.OwnGlobals.ToApi(),
                newOwnPerms.ToApi(),
                existingData.PairInitAt,
                existingData.PairTempAccepter
            );
            await Clients.User(uid).Callback_RemoveRequest(Extensions.ToApiRemoval(new(uid), new(UserUID))).ConfigureAwait(false);
            await Clients.User(uid).Callback_AddPair(requesterRetDto).ConfigureAwait(false);
            await Clients.User(uid).Callback_UserOnline(new(callerUser.ToUserData(), UserCharaIdent)).ConfigureAwait(false);
        }

        // Inc the metrics and then return result.
        _metrics.IncCounter(MetricsAPI.CounterRequestsAccepted);
        _metrics.IncCounter(MetricsAPI.CounterRequestsAccepted);
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending);

        var retValue = new AddedUserPair(callerRetDto, requesterIdentity != null ? new OnlineUser(target.ToUserData(), requesterIdentity) : null);
        return HubResponseBuilder.Yippee(retValue);
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<AddedUserPair>>> UserAcceptRequests(UserListDto toAccept)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(toAccept));
        var senderUids = toAccept.Users.Select(u => u.UID.Trim()).ToList();
        senderUids.Remove(UserUID); // Pre-fire removing self

        if (senderUids.Count == 0)
            return HubResponseBuilder.AwDangIt<List<AddedUserPair>>(SundouleiaApiEc.NullData);

        // Single query: requests + user + globals
        var requests = await GetRespondingRequests(senderUids).ConfigureAwait(false);

        if (requests.Count == 0)
            return HubResponseBuilder.AwDangIt<List<AddedUserPair>>(SundouleiaApiEc.NullData);

        // Build what we are returning.
        var now = DateTime.UtcNow;
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        var onlineUsers = await GetOnlineUsers(senderUids).ConfigureAwait(false);
        
        var addedPairs = new List<AddedUserPair>(requests.Count);

        // Process each request one by one, without extra queries.
        foreach (var (senderUid, info) in requests)
        {
            var request = info.Request;
            var wasTemp = request.IsTemporary;

            // Create pairs
            var callerToTarget = new ClientPair
            {
                User = callerUser,
                OtherUser = info.Sender,
                CreatedAt = now,
                TempAccepterUID = wasTemp ? UserUID : string.Empty
            };

            var targetToCaller = new ClientPair
            {
                User = info.Sender,
                OtherUser = callerUser,
                CreatedAt = now,
                TempAccepterUID = wasTemp ? UserUID : string.Empty
            };

            DbContext.ClientPairs.AddRange(callerToTarget, targetToCaller);

            // Create perms (always new in bulk accept)
            var ownPerms = new ClientPairPermissions
            {
                User = callerUser,
                OtherUser = info.Sender,
                PauseVisuals = false,
                AllowAnimations = info.RecipientGlobals.DefaultAllowAnimations,
                AllowSounds = info.RecipientGlobals.DefaultAllowSounds,
                AllowVfx = info.RecipientGlobals.DefaultAllowVfx,
                MoodleAccess = info.RecipientGlobals.DefaultMoodleAccess,
                MaxMoodleTime = info.RecipientGlobals.DefaultMaxMoodleTime,
                ShareOwnMoodles = info.RecipientGlobals.DefaultShareOwnMoodles,
            };

            var otherPerms = new ClientPairPermissions
            {
                User = info.Sender,
                OtherUser = callerUser,
                PauseVisuals = false,
                AllowAnimations = info.SenderGlobals.DefaultAllowAnimations,
                AllowSounds = info.SenderGlobals.DefaultAllowSounds,
                AllowVfx = info.SenderGlobals.DefaultAllowVfx,
                MoodleAccess = info.SenderGlobals.DefaultMoodleAccess,
                MaxMoodleTime = info.SenderGlobals.DefaultMaxMoodleTime,
                ShareOwnMoodles = info.SenderGlobals.DefaultShareOwnMoodles,
            };

            DbContext.ClientPairPerms.AddRange(ownPerms, otherPerms);
            DbContext.Requests.Remove(request);

            // Build return DTO (caller perspective)
            var callerRetDto = new UserPair(
                request.User!.ToUserData(),
                ownPerms.ToApi(),
                info.SenderGlobals.ToApi(),
                otherPerms.ToApi(),
                DateTime.UtcNow,
                wasTemp ? UserUID : string.Empty
            );

            // Inform other if online.
            if (onlineUsers.TryGetValue(senderUid, out var ident))
            {
                // Online user.
                addedPairs.Add(new(callerRetDto, new(info.Sender.ToUserData(), ident)));
                // Return for the request sender.
                var senderRetDto = new UserPair(
                    callerUser.ToUserData(),
                    otherPerms.ToApi(),
                    info.RecipientGlobals.ToApi(),
                    ownPerms.ToApi(),
                    DateTime.UtcNow,
                    wasTemp ? UserUID : string.Empty
                );
                await Clients.User(senderUid).Callback_RemoveRequest(Extensions.ToApiRemoval(new(senderUid), new(UserUID))).ConfigureAwait(false);
                await Clients.User(senderUid).Callback_AddPair(senderRetDto).ConfigureAwait(false);
                await Clients.User(senderUid).Callback_UserOnline(new(callerUser.ToUserData(), UserCharaIdent)).ConfigureAwait(false);
            }
            // Otherwise, simply add.
            else
            {
                addedPairs.Add(new(callerRetDto, null));
            }
            // Get if the request creator is online of not.
            var requesterIdentity = await GetUserIdent(senderUid).ConfigureAwait(false);
            // If the request creator is online, send them the remove request and add pair callbacks, and also return sendOnline to both.
            if (requesterIdentity is not null)
            {
                var requesterRetDto = new UserPair(
                    callerUser.ToUserData(),
                    otherPerms.ToApi(),
                    info.RecipientGlobals.ToApi(),
                    ownPerms.ToApi(),
                    DateTime.UtcNow,
                    wasTemp ? UserUID : string.Empty
                );
                await Clients.User(senderUid).Callback_RemoveRequest(Extensions.ToApiRemoval(new(senderUid), new(UserUID))).ConfigureAwait(false);
                await Clients.User(senderUid).Callback_AddPair(requesterRetDto).ConfigureAwait(false);
                await Clients.User(senderUid).Callback_UserOnline(new(callerUser.ToUserData(), UserCharaIdent)).ConfigureAwait(false);
            }
        }

        // Update metrics and return.
        _metrics.IncCounter(MetricsAPI.CounterRequestsAccepted, requests.Count);
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending, requests.Count);

        return HubResponseBuilder.Yippee(addedPairs);
    }

    /// <summary>
    ///     Whenever a pending request is rejected by the target recipient. <para />
    ///     You are expected to remove the request from your pending list if successful. (Helps save extra server calls)
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserRejectRequest(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        var requesterUid = dto.User.UID.Trim();

        // Prevent rejecting requests that do not exist.
        if (await DbContext.Requests.AsNoTracking().SingleOrDefaultAsync(r => r.UserUID == requesterUid && r.OtherUserUID == UserUID).ConfigureAwait(false) is not { } request)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.RequestNotFound);

        // See if we need to return the rejection request to the requester if they are online.
        if (await GetUserIdent(dto.User.UID).ConfigureAwait(false) is not null)
        {
            SundesmoRequest rejectedRequest = request.ToApi();
            await Clients.User(dto.User.UID).Callback_RemoveRequest(rejectedRequest).ConfigureAwait(false);
        }

        // Remove from DB and save changes.
        DbContext.Requests.Remove(request);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterRequestsRejected);
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserRejectRequests(UserListDto toReject)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(toReject));
        var requesterUids = toReject.Users.Select(u => u.UID.Trim()).ToHashSet(StringComparer.Ordinal);

        // Fetch all requests where the current user is the target.
        var dbRequests = await DbContext.Requests
            .AsNoTracking()
            .Where(r => r.OtherUserUID == UserUID && requesterUids.Contains(r.UserUID))
            .ToListAsync()
            .ConfigureAwait(false);

        // If none exist, return not found, note that even if not all request exists,
        // the caller should still remove all after, as they no longer exist on the server.
        if (dbRequests.Count == 0)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.RequestNotFound);

        // Notify each requester if they are online.
        foreach (var request in dbRequests)
        {
            if (await GetUserIdent(request.UserUID).ConfigureAwait(false) is not null)
                await Clients.User(request.UserUID).Callback_RemoveRequest(request.ToApi()).ConfigureAwait(false);
        }

        // Remove all requests in bulk.
        DbContext.Requests.RemoveRange(dbRequests);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterRequestsRejected, dbRequests.Count);
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending, dbRequests.Count);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPersistPair(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // If the pair does not yet exist, fail.
        if (await DbContext.ClientPairs.AsNoTracking().SingleOrDefaultAsync(p => p.UserUID == UserUID && p.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } pairEntry)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);
        
        // If the pair is not temporary, fail.
        if (string.IsNullOrEmpty(pairEntry.TempAccepterUID))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyPermanent);

        // If the caller is not the temporary accepter, fail.
        if (!string.Equals(pairEntry.TempAccepterUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Make both sides permanent.
        var otherEntry = await DbContext.ClientPairs.SingleAsync(p => p.UserUID == dto.User.UID && p.OtherUserUID == UserUID).ConfigureAwait(false);

        pairEntry.TempAccepterUID = string.Empty;
        otherEntry.TempAccepterUID = string.Empty;

        // Update the DB.
        DbContext.ClientPairs.Update(pairEntry);
        DbContext.ClientPairs.Update(otherEntry);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Notify the other user that they are now permanent if they are online.
        if (await GetUserIdent(dto.User.UID).ConfigureAwait(false) is not null)
            await Clients.User(dto.User.UID).Callback_PersistPair(new(new(UserUID))).ConfigureAwait(false);

        // return success to the caller, informing them they can update this pair to permanent.
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     When the caller wishes to remove the specified user from their client pairs. <para />
    ///     If successful, you should remove the pair from your list of pairs.
    /// </summary>
    /// <remarks> THIS SHOULD ENSURE THAT THE PAIR PERMISSIONS FOR A CLIENT PAIR ARE ALWAYS DELETED PROPERLY!!!</remarks>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserRemovePair(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent removing self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Prevent processing if not paired.
        if (await DbContext.ClientPairs.AsNoTracking().SingleOrDefaultAsync(w => w.UserUID == UserUID && w.OtherUserUID == dto.User.UID).ConfigureAwait(false) is not { } callerPair)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotPaired);

        // Retrieve the additional info for the connection between the caller and target.
        UserInfo? pairData = await GetPairInfo(UserUID, dto.User.UID).ConfigureAwait(false);

        // Remove the caller -> Target relation table entries.
        DbContext.ClientPairs.Remove(callerPair);
        if (pairData?.OwnPerms is not null) DbContext.ClientPairPerms.Remove(pairData.OwnPerms);

        // Remove the target -> Caller relation table entries.
        if (await DbContext.ClientPairs.AsNoTracking().SingleOrDefaultAsync(w => w.UserUID == dto.User.UID && w.OtherUserUID == UserUID).ConfigureAwait(false) is { } otherPair)
        {
            DbContext.ClientPairs.Remove(otherPair);
            if (pairData?.OtherPerms is not null) DbContext.ClientPairPerms.Remove(pairData.OtherPerms);
        }

        // Update DB.
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // If the target is online, send to them the remove pair callback.
        if (await GetUserIdent(dto.User.UID).ConfigureAwait(false) is not null)
            await Clients.User(dto.User.UID).Callback_RemovePair(new(new(UserUID))).ConfigureAwait(false);

        return HubResponseBuilder.Yippee();
    }

    public async Task<HubResponse> UserRemovePairs(UserListDto toRemove)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(toRemove));
        // Hashset for O(1) lookup efficiency.
        var targetUids = toRemove.Users.Select(u => u.UID.Trim()).ToHashSet(StringComparer.Ordinal);
        targetUids.Remove(UserUID);

        // "Then draw the rest of the picture"
        var pairsAndPerms = await (
            from up in DbContext.ClientPairs.AsNoTracking().Where(cp => cp.UserUID == UserUID && targetUids.Contains(cp.OtherUserUID))
            join tp in DbContext.ClientPairs.AsNoTracking().Where(cp => cp.OtherUserUID == UserUID && targetUids.Contains(cp.UserUID))
                on up.OtherUserUID equals tp.UserUID into joined
            from tp in joined.DefaultIfEmpty()

            join ownperm in DbContext.ClientPairPerms.AsNoTracking().Where(p => p.UserUID == UserUID && targetUids.Contains(p.OtherUserUID))
                on new { up.UserUID, up.OtherUserUID } equals new { ownperm.UserUID, ownperm.OtherUserUID } into ownperms
            from ownperm in ownperms.DefaultIfEmpty()

            join otherperm in DbContext.ClientPairPerms.AsNoTracking().Where(p => p.OtherUserUID == UserUID && targetUids.Contains(p.UserUID))
                on new { tp.UserUID, tp.OtherUserUID } equals new { otherperm.UserUID, otherperm.OtherUserUID } into otherperms
            from otherperm in otherperms.DefaultIfEmpty()

            select new
            {
                OtherUid = up.OtherUserUID,
                CallerPair = up,
                TargetPair = tp,
                OwnPerms = ownperm,
                OtherPerms = otherperm
            }
        ).ToListAsync().ConfigureAwait(false);

        if (pairsAndPerms.Count == 0)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Bulk Remove.
        DbContext.ClientPairs.RemoveRange(pairsAndPerms.Select(x => x.CallerPair));
        DbContext.ClientPairs.RemoveRange(pairsAndPerms.Where(x => x.TargetPair != null).Select(x => x.TargetPair!));
        DbContext.ClientPairPerms.RemoveRange(pairsAndPerms.Where(x => x.OwnPerms != null).Select(x => x.OwnPerms!));
        DbContext.ClientPairPerms.RemoveRange(pairsAndPerms.Where(x => x.OtherPerms != null).Select(x => x.OtherPerms!));
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform Online Users of the removal.
        if (pairsAndPerms.Count > 0)
            await Clients.Users(pairsAndPerms.Select(p => p.OtherUid)).Callback_RemovePair(new(new(UserUID))).ConfigureAwait(false);

        return HubResponseBuilder.Yippee();
    }

    /// <summary> 
    ///     When the caller wishes to commit seppuku to their profile. <para/>
    ///     If the profile being deleted is the account's primary profile,
    ///     all secondary profiles are deleted with it.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserDelete()
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args());

        // Prevent deleting that which is already deleted.
        if (await DbContext.Users.AsNoTracking().SingleOrDefaultAsync(a => a.UID == UserUID).ConfigureAwait(false) is not { } caller)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Delete the profile(s) from the database, and obtain a dictionary of all the removed profiles and their paired UID's.
        var pairRemovals = await SharedDbFunctions.DeleteUserProfile(caller, _logger.Logger, DbContext, _metrics).ConfigureAwait(false);

        // pairRemovals is a dict of each uid that was removed, and all of those uid's pairs that should receive Callback_RemovePair
        foreach (var (deletedProfile, profilePairUids) in pairRemovals)
            await Clients.Users(profilePairUids).Callback_RemovePair(new(new(deletedProfile))).ConfigureAwait(false);

        return HubResponseBuilder.Yippee();
    }


    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserBlock(UserDto dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // Prevent blocking self.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // recipient must exist.
        if (await DbContext.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UID == dto.User.UID).ConfigureAwait(false) is not { } target)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // ensure the entry does not yet exist already.
        if (await DbContext.BlockedUsers.AsNoTracking().AnyAsync(b => b.UserUID == UserUID && b.OtherUserUID == target.UID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.RecipientBlocked);

        // Create the block entry.
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        var blockEntry = new BlockedUser()
        {
            User = callerUser,
            OtherUser = target,
        };
        await DbContext.BlockedUsers.AddAsync(blockEntry).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterUsersBlocked);
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     Unblock a blocked user.
    /// </summary>
    public async Task<HubResponse> UserUnblock(UserDto user)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(user));
        // Prevent target being self
        if (string.Equals(user.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Blocked Entry must exist.
        if (await DbContext.BlockedUsers.AsNoTracking().SingleOrDefaultAsync(b => b.UserUID == UserUID && b.OtherUserUID == user.User.UID).ConfigureAwait(false) is not { } blockEntry)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidRecipient);

        // Remove the entry and save changes.
        DbContext.BlockedUsers.Remove(blockEntry);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterUsersUnblocked);
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     Triggered whenever a sundesmo unloads the plugin or hard reconnects from the server. <para />
    ///     Not triggered on normal reconnections or soft disconnects.
    /// </summary>
    /// <remarks> Could pass in the online paired UID's to avoid the extra lookup. </remarks>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserNotifyIsUnloading()
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args());
        await SendIsUnloadingToAllPairedUsers().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }
}