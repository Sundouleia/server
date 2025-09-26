using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;

namespace SundouleiaServer.Hubs;
#nullable enable

public partial class SundouleiaHub
{
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<PendingRequest>> UserSendRequest(CreateRequest dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        // The target to send the request to.
        string uid = dto.User.UID.Trim();

        // Prevent sending requests to self.
        if (string.Equals(uid, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.InvalidRecipient);

        // Prevent sending to a user not registered in Sundouleia.
        if (await DbContext.Users.SingleOrDefaultAsync(u => u.UID == uid).ConfigureAwait(false) is not { } target)
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Warning, $"Cannot send Request to {dto.User.UID}, they don't exist").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.InvalidRecipient);
        }

        // Sort the following calls by estimated tables with least entries to most entries for efficiency.

        // Prevent sending a request if either end has blocked the other.
        if (await DbContext.BlockedUsers.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.RecipientBlocked);

        // Ensure that the request does not already exist in the database.
        if (await DbContext.Requests.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.RequestExists);

        // Prevent sending a request if already paired.
        if (await DbContext.ClientPairs.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.AlreadyPaired);

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
            return HubResponseBuilder.AwDangIt<PendingRequest>(SundouleiaApiEc.RequestExists);

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

    /// <summary> 
    ///     Triggered whenever the recipient of a request accepts it. <para />
    ///     Bare in mind that due to the way this is called, the person accepting is the request entry <b>TARGET</b>. <para />
    ///     
    ///     If the caller receives the EC "AlreadyPaired", they should remove the request from their pending list.
    /// </summary>
    /// <returns> The new UserPair to add, if the request was properly accepted.</returns>
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

        // Must not be already paired. If you are, discard the request regardless, but return with error. 
        if (await DbContext.ClientPairs.AnyAsync(p => (p.UserUID == UserUID && p.OtherUserUID == target.UID) || (p.UserUID == target.UID && p.OtherUserUID == UserUID)).ConfigureAwait(false))
        {
            DbContext.Requests.Remove(request);
            await DbContext.SaveChangesAsync().ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt<AddedUserPair>(SundouleiaApiEc.AlreadyPaired);
        }

        // Request is properly accepted, create the relationship pair.
        var callerUser = await DbContext.Users.SingleAsync(u => u.UID == UserUID).ConfigureAwait(false);
        var callerToRecipient = new ClientPair() { User = callerUser, OtherUser = target, };
        var recipientToCaller = new ClientPair() { User = target, OtherUser = callerUser, };
        // Add them to the DB.
        await DbContext.ClientPairs.AddAsync(callerToRecipient).ConfigureAwait(false);
        await DbContext.ClientPairs.AddAsync(recipientToCaller).ConfigureAwait(false);

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
            };
            await DbContext.ClientPairPerms.AddAsync(newOtherPerms).ConfigureAwait(false);
        }

        // Remove the request and save the changes.
        DbContext.Requests.Remove(request);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        
        // Compile together the UserPair we will return to the caller.
        // This is the UserPair of the Caller (Request Accepter) -> Target (Request Creator)
        var callerRetDto = new UserPair(target.ToUserData(), newOwnPerms.ToApi(), existingData!.OtherGlobals.ToApi(), newOtherPerms.ToApi());

        // Get if the request creator is online of not.
        var requesterIdentity = await GetUserIdent(uid).ConfigureAwait(false);

        // If the request creator is online, send them the remove request and add pair callbacks, and also return sendOnline to both.
        if (requesterIdentity is not null)
        {
            var requesterRetDto = new UserPair(callerUser.ToUserData(), newOtherPerms.ToApi(), existingData!.OwnGlobals.ToApi(), newOwnPerms.ToApi());
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
            PendingRequest rejectedRequest = request.ToApi();
            await Clients.User(dto.User.UID).Callback_RemoveRequest(rejectedRequest).ConfigureAwait(false);
        }

        // Remove from DB and save changes.
        DbContext.Requests.Remove(request);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterRequestsRejected);
        _metrics.DecGauge(MetricsAPI.GaugeRequestsPending);
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     When the caller wishes to remove the specified user from their client pairs. <para />
    ///     If successful, you should remove the pair from your list of pairs.
    /// </summary>
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
}