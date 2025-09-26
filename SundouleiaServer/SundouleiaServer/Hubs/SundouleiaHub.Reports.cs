using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;

namespace SundouleiaServer.Hubs;

public partial class SundouleiaHub
{
    /// <summary>
    ///     Whenever a Profile is reported by another user.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserReportProfile(ProfileReport dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent self-reporting.
        if (string.Equals(dto.User.UID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.CannotInteractWithSelf);

        // Prevent duplicate reports.
        if (await DbContext.ProfileReports.AsNoTracking().AnyAsync(r => r.ReportedUserUID == dto.User.UID && r.ReportingUserUID == UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyReported); 

        // Prevent reporting a profile that no longer exists.
        if (await DbContext.UserProfileData.AsNoTracking().SingleOrDefaultAsync(u => u.UserUID == dto.User.UID).ConfigureAwait(false) is not { } profile)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.ProfileNotFound);

        // Report is valid, so construct the report to send off.
        var reportToAdd = new ReportedUserProfile()
        {
            ReportTime = DateTime.UtcNow,
            SnapshotIsNSFW = profile.IsNSFW,
            SnapshotImage = profile.Base64AvatarData,
            SnapshotDescription = profile.Description,
            
            ReportingUserUID = UserUID,
            ReportedUserUID = dto.User.UID,
            ReportReason = dto.ReportReason,
        };
        await DbContext.ProfileReports.AddAsync(reportToAdd).ConfigureAwait(false);

        // Mark the profile as flagged and update that as well.
        profile.FlaggedForReport = true;
        DbContext.UserProfileData.Update(profile);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform the reporter that the report was successful.
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Your Report was processed, and now pending validation from CK").ConfigureAwait(false);
        
        // Push a profile update to all users besides the reported user.
        var pairsOfReportedUser = await GetPairedUnpausedUsers(dto.User.UID).ConfigureAwait(false);
        var onlinePairs = await GetOnlineUsers(pairsOfReportedUser).ConfigureAwait(false);
        IEnumerable<string> onlineUids = onlinePairs.Keys;

        if (!onlineUids.Contains(UserUID, StringComparer.Ordinal))
            await Clients.Caller.Callback_ProfileUpdated(new(dto.User)).ConfigureAwait(false);
        await Clients.Users(onlineUids).Callback_ProfileUpdated(new(dto.User)).ConfigureAwait(false);
        
        _metrics.IncCounter(MetricsAPI.CounterReportsCreatedProfile);
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     Whenever a radar area is reported for misconduct.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserReportRadar(RadarReport dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent duplicate reports.
        if (await DbContext.RadarReports.AsNoTracking().AnyAsync(r => r.WorldId == dto.WorldId && r.TerritoryId == r.TerritoryId && r.Kind == ReportKind.Radar).ConfigureAwait(false))
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "This Radar zone is already under investigation.").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyReported);
        }

        // Report is valid, so construct the report to send off.
        var reportToAdd = new ReportedRadar()
        {
            Kind = ReportKind.Radar,
            ReportTime = DateTime.UtcNow,
            WorldId = dto.WorldId,
            TerritoryId = dto.TerritoryId,
            
            RecentRadarChatHistory = string.Empty,
            ReportedUserUID = string.Empty,

            IsIndoor = dto.IsIndoor,
            ApartmentDivision = dto.IsIndoor ? dto.ApartmentDivision : (byte)0,
            PlotIndex = dto.IsIndoor ? dto.PlotIndex : (byte)0,
            WardIndex = dto.IsIndoor ? dto.WardIndex : (byte)0,
            RoomNumber = dto.IsIndoor ? dto.RoomNumber : (byte)0,

            ReporterUID = UserUID,

            ReportReason = dto.ReportReason,
        };
        await DbContext.RadarReports.AddAsync(reportToAdd).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform the reporter that the report was successful.
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Your Report was processed, and now pending validation from CK").ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterReportsCreatedRadar);
        return HubResponseBuilder.Yippee();
    }

    /// <summary>
    ///     Whenever a user is reported for misconduct in a radar chat.
    /// </summary>
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserReportChat(RadarChatReport dto)
    {
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));

        // Prevent reporting non-existent users.
        if (await DbContext.Auth.Include(a => a.AccountRep).AsNoTracking().SingleOrDefaultAsync(a => a.UserUID == dto.User.UID).ConfigureAwait(false) is not { } auth)
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "The user you are reporting does not exist.").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);
        }

        // Prevent duplicate reports.
        if (await DbContext.RadarReports.AsNoTracking().AnyAsync(r => r.WorldId == dto.WorldId && r.TerritoryId == r.TerritoryId && r.Kind == ReportKind.Chat).ConfigureAwait(false))
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "This Radar zone is already under investigation.").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyReported);
        }

        // Report is valid, so construct the report to send off.
        var reportToAdd = new ReportedRadar()
        {
            Kind = ReportKind.Chat,
            ReportTime = DateTime.UtcNow,
            WorldId = dto.WorldId,
            TerritoryId = dto.TerritoryId,
            // If people make fake chat log histories we can always just cache the recent history
            // from the various groups and regurgitate them for snapshots in the reports.
            RecentRadarChatHistory = dto.ChatCompressed,
            ReportedUserUID = dto.User.UID,

            IsIndoor = dto.IsIndoor,
            ApartmentDivision = dto.IsIndoor ? dto.ApartmentDivision : (byte)0,
            PlotIndex = dto.IsIndoor ? dto.PlotIndex : (byte)0,
            WardIndex = dto.IsIndoor ? dto.WardIndex : (byte)0,
            RoomNumber = dto.IsIndoor ? dto.RoomNumber : (byte)0,

            ReporterUID = UserUID,

            ReportReason = dto.ReportReason,
        };
        await DbContext.RadarReports.AddAsync(reportToAdd).ConfigureAwait(false);
        
        // Inform the reporter that the report was successful.
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Your Report was processed, and now pending validation from CK").ConfigureAwait(false);

        // In the case that the reported user was being vulgar in chat, we will want to clear all messages sent by this user.
        // To do so, run a callback to all users in this group.
        var reportedChatGroup = _radarService.GetGroupName(dto.WorldId, dto.TerritoryId);
        // This will also inform the reported user, helping them know they disconnected.
        await Clients.Group(reportedChatGroup).Callback_RadarUserFlagged(dto.User.UID).ConfigureAwait(false);

        // Remove the reported user from the chat group, preventing them from getting any further messages.
        if (_userConnections.TryGetValue(dto.User.UID, out string connectionId) && _radarService.IsInChat(connectionId))
            await _radarService.LeaveRadarChat(connectionId).ConfigureAwait(false);

        // Disable chat for the reported user until it is resolved.
        auth.AccountRep.ChatUsage = false;
        DbContext.Auth.Update(auth);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterReportsCreatedRadarChat);
        return HubResponseBuilder.Yippee();
    }
}

