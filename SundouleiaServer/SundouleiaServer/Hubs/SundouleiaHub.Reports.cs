using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;
using System.Text.Json;

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
            // All that is needed for a radarGroup report.
            WorldId = dto.WorldId,
            TerritoryId = dto.TerritoryId,
            
            ChatLogId = string.Empty,
            MessageId = string.Empty,
            Message = string.Empty,
            ChatContextJson = string.Empty,
            
            ReportedUserUID = string.Empty,
            ReporterUID = UserUID,
            ReportReason = dto.ReportReason,
        };

        await DbContext.RadarReports.AddAsync(reportToAdd).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform the reporter that the report was successful.
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Your Report was processed, and now pending validation from Sundouleia").ConfigureAwait(false);

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
        if (await DbContext.RadarReports.AsNoTracking().AnyAsync(r => r.ChatLogId == dto.Chatlog.ChatId && r.MessageId == dto.MessageId && r.Kind == ReportKind.Chat).ConfigureAwait(false))
        {
            await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "This RadarChat is already under investigation.").ConfigureAwait(false);
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.AlreadyReported);
        }

        // Collect up the server-authorized chat messages
        var serverChatHistory = _chatService.GetMessageHistory(dto.Chatlog.ChatId, 25);
        var toSerialize = serverChatHistory
            .Select(m => new JsonChatMsg(m.MsgId, m.TimeSentUTC, m.Sender.UID, m.Sender.DisplayName, m.Message))
            .ToArray();

        var snapshotJson = JsonSerializer.Serialize(toSerialize, new JsonSerializerOptions(JsonSerializerDefaults.General) { WriteIndented = false });
        // Report is valid, so construct the report to send off.
        var reportToAdd = new ReportedRadar()
        {
            Kind = ReportKind.Chat,
            ReportTime = DateTime.UtcNow,
            WorldId = 0,
            TerritoryId = 0,
            ChatLogId = dto.Chatlog.ChatId,
            MessageId = dto.MessageId,
            Message = toSerialize.FirstOrDefault(m => string.Equals(m.MsgId, dto.MessageId, StringComparison.Ordinal)).Message ?? string.Empty,
            ChatContextJson = snapshotJson,
            ReportedUserUID = dto.User.UID,
            
            ReporterUID = UserUID,
            ReportReason = dto.Reason,
        };
        await DbContext.RadarReports.AddAsync(reportToAdd).ConfigureAwait(false);
        
        // Inform the reporter that the report was successful.
        await Clients.Caller.Callback_ServerMessage(MessageSeverity.Information, "Your Report was processed, and now pending validation from CK").ConfigureAwait(false);

        // Disable chat for the reported user until it is resolved.
        auth.AccountRep.ChatUsage = false;
        DbContext.Auth.Update(auth);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        _metrics.IncCounter(MetricsAPI.CounterReportsCreatedRadarChat);
        return HubResponseBuilder.Yippee();
    }
}

