
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaServer.Hubs;
using SundouleiaShared.Data;
using SundouleiaShared.Models;
using SundouleiaShared.Services;
using SundouleiaShared.Utils.Configuration;
using System.Globalization;
using ServerDiscordConfig = SundouleiaShared.Utils.Configuration.DiscordConfig;

namespace SundouleiaDiscord;

public partial class ReportWizard : InteractionModuleBase
{
    private readonly ILogger<ReportWizard> _logger;
    private readonly IServiceProvider _services;
    private readonly DiscordBotServices _botServices;
    private readonly IConfigurationService<ServerConfiguration> _gsConfigService;
    private readonly IConfigurationService<ServerDiscordConfig> _discordConfig;
    private readonly IHubContext<SundouleiaHub> _hubContext;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDbContextFactory<SundouleiaDbContext> _dbContextFactory;

    public ReportWizard(ILogger<ReportWizard> logger, IServiceProvider services, DiscordBotServices botServices,
        IConfigurationService<ServerConfiguration> gsConfig, IConfigurationService<ServerDiscordConfig> discordConfig,
        IHubContext<SundouleiaHub> hubContent, IConnectionMultiplexer multiplexer, 
        IDbContextFactory<SundouleiaDbContext> dbContextFactory)
    {
        _logger = logger;
        _services = services;
        _botServices = botServices;
        _gsConfigService = gsConfig;
        _discordConfig = discordConfig;
        _hubContext = hubContent;
        _multiplexer = multiplexer;
        _dbContextFactory = dbContextFactory;
    }

    [ComponentInteraction("reports-refresh")]
    public async Task RefreshReportWizard()
    {
        if (Context.Interaction is not SocketMessageComponent component || component.Channel is not SocketTextChannel channel)
            return;

        IUserMessage message = component.Message;
        // Run the same CreateOrUpdateReportWizard, but pass the message to update in-place
        await _botServices.CreateOrUpdateReportWizardWithExistingMessage(channel, message).ConfigureAwait(false);
        // Acknowledge the button to prevent "interaction failed"
        await component.DeferAsync().ConfigureAwait(false);
    }

    // Launcher for the Report Wizard
    [ComponentInteraction("reports-profile-home:*")]
    public async Task ProfileReportWizardHome(bool init = false)
    {
        // if the interaction was not valid, then return.
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        // Interaction was successful, so log it. 
        _logger.LogInformation("{method}:{userId}", nameof(ProfileReportWizardHome), Context.Interaction.User.Id);

        // fetch the database context to see if they already have a claimed account.
        using var db = await GetDbContext().ConfigureAwait(false);
        var reports = db.ProfileReports.ToList();
        var eb = new EmbedBuilder()
            .WithTitle("Profile Reports")
            .WithColor(Color.DarkRed)
            .WithDescription(reports.Count is 0 ? "⚠️ No active reports." : "📖 Select a report to review.");

        var cb = new ComponentBuilder();
        if (reports.Count > 0)
        {
            var select = new SelectMenuBuilder()
                .WithCustomId("reports-profile-selector")
                .WithPlaceholder("Select a report");

            foreach (var report in reports.Take(25))
                select.AddOption(label: report.ReportedUserUID, value: $"{report.ReportID}", description: $"Reported by {report.ReportingUserUID}");

            cb.WithSelectMenu(select); // only add if >0 options
        }

        // if this message is being generated in response to the user pressing "Start" on the initial message,
        // send the message as an ephemeral message, meaning a reply personalized so only the user can see it.
        if (init)
        {
            await RespondAsync(embed: eb.Build(), components: cb.Build(), ephemeral: true).ConfigureAwait(false);
            IUserMessage resp = await GetOriginalResponseAsync().ConfigureAwait(false);
            // Validate the interaction for this user as their current ephemeral interaction.
            _botServices.ValidInteractions[Context.User.Id] = resp.Id;
            _logger.LogInformation($"Emphemeral Interaction Started: {resp.Id}");
        }
        else
        {
            await ModifyInteraction(eb, cb).ConfigureAwait(false);
        }
    }

    [ComponentInteraction("reports-profile-selector")]
    public async Task SelectionProfileReport(string id)
    {
        if (!await ValidateInteraction().ConfigureAwait(false))
            return;

        // Load the report with all related data
        if (!int.TryParse(id, CultureInfo.InvariantCulture, out var reportId))
        {
            await RespondAsync("Invalid report ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Get the report details and render the report.
        using var db = await GetDbContext().ConfigureAwait(false);

        var reportData = await GetProfileReport(db, reportId).ConfigureAwait(false);

        var eb = new EmbedBuilder();
        var cb = new ComponentBuilder();
        var attachments = new List<FileAttachment>();
        var streamsToDispose = new List<MemoryStream>();
        try
        {
            await RenderProfileReport(eb, reportData, attachments, streamsToDispose).ConfigureAwait(false);

            AddProfileReportActionSelector(cb, reportId);
            AddProfileHome(cb);

            await ModifyInteraction(eb, cb, attachments).ConfigureAwait(false);
        }
        finally
        {
            // Dispose of the render streams
            foreach (var stream in streamsToDispose)
                stream.Dispose();
        }
    }

    // Helper to render a report into an embed
    private async Task RenderProfileReport(EmbedBuilder eb, ReportedProfileInfo data, List<FileAttachment> attachments, List<MemoryStream> streams)
    {
        // Title + Color
        eb.WithTitle($"Report: {data.Report.ReportID}")
          .WithColor(Color.Magenta);

        // Time of report
        var reportTime = new DateTimeOffset(data.Report.ReportTime, TimeSpan.Zero);
        eb.AddField("Report Time", string.Create(CultureInfo.InvariantCulture, $"<t:{reportTime.ToUnixTimeSeconds()}:F>"));

        // Users + Discord IDs
        string reportedDisplay = data.ReportedAuth?.DiscordId != null
            ? $"{data.Report.ReportedUserUID} (<@{data.ReportedAuth.DiscordId}>)"
            : data.Report.ReportedUserUID;

        string reporterDisplay = data.ReporterAuth?.DiscordId != null
            ? $"{data.Report.ReportingUserUID} (<@{data.ReporterAuth.DiscordId}>)"
            : data.Report.ReportingUserUID;

        eb.AddField("Reported User", reportedDisplay, inline: true);
        eb.AddField("Reporter User", reporterDisplay, inline: true);

        // Profile description + Report reason
        eb.AddField("Profile Description", string.IsNullOrWhiteSpace(data.Report.SnapshotDescription)
            ? "-" : data.Report.SnapshotDescription);

        eb.AddField("Report Reason", string.IsNullOrWhiteSpace(data.Report.ReportReason)
                ? "-" : data.Report.ReportReason);

        // Conditionally add the reported snapshot image
        if (!string.IsNullOrEmpty(data.Report.SnapshotImage))
        {
            eb.AddField("Snapshotted Image", "ProfilePic at the time of the report." +
                "\nTop-Right Image is how it's displayed now.");
            var reportedImgName = $"{data.Report.ReportedUserUID}_profile_reported_{Guid.NewGuid().ToString("N")}.png";

            var reportedImageStream = new MemoryStream(Convert.FromBase64String(data.Report.SnapshotImage));
            streams.Add(reportedImageStream);
            var reportedImageAttachment = new FileAttachment(reportedImageStream, reportedImgName);
            attachments.Add(reportedImageAttachment);
            eb.WithImageUrl($"attachment://{reportedImgName}");
        }
        // Conditionally add the current profile picture as thumbnail
        if (!string.IsNullOrEmpty(data.Profile.Base64AvatarData))
        {
            var reportedCurImgName = $"{data.Report.ReportedUserUID}_profile_current_{Guid.NewGuid().ToString("N")}.png";
            var profileImageStream = new MemoryStream(Convert.FromBase64String(data.Profile.Base64AvatarData));
            streams.Add(profileImageStream);

            var profileImageAttachment = new FileAttachment(profileImageStream, reportedCurImgName);
            attachments.Add(profileImageAttachment);
            eb.WithThumbnailUrl($"attachment://{reportedCurImgName}");
        }
    }

    // Helper to add action buttons to the report
    private void AddProfileReportActionSelector(ComponentBuilder cb, int reportId)
    {
        cb.WithButton("Dismiss", customId: $"report-profile-action-dismiss-{reportId}", style: ButtonStyle.Primary);
        cb.WithButton("Strike Viewing", customId: $"report-profile-action-strike-viewing-{reportId}", style: ButtonStyle.Secondary);
        cb.WithButton("Strike Editing", customId: $"report-profile-action-strike-editing-{reportId}", style: ButtonStyle.Secondary);
        cb.WithButton("Ban Viewing", customId: $"report-profile-action-ban-viewing-{reportId}", style: ButtonStyle.Danger);
        cb.WithButton("Ban Editing", customId: $"report-profile-action-ban-editing-{reportId}", style: ButtonStyle.Danger);
        cb.WithButton("Flag Reporter", customId: $"report-profile-action-flag-reporter-{reportId}", style: ButtonStyle.Danger);
    }

    private void AddChatReportActionSelector(ComponentBuilder cb, int reportId)
    {
        cb.WithButton("Dismiss", customId: $"report-chat-action-dismiss-{reportId}", style: ButtonStyle.Primary);
        cb.WithButton("Strike Usage", customId: $"report-chat-action-strike-{reportId}", style: ButtonStyle.Secondary);
        cb.WithButton("Ban Usage", customId: $"report-chat-action-ban-{reportId}", style: ButtonStyle.Danger);
        cb.WithButton("Flag Reporter", customId: $"report-chat-action-flag-reporter-{reportId}", style: ButtonStyle.Danger);
    }

    // Dismissing reports.
    [ComponentInteraction("report-profile-action-dismiss-*")]
    public async Task DismissProfileReport(string reportId)
    {
        if (!await ValidateInteraction().ConfigureAwait(false))
            return;

        if (!int.TryParse(reportId, CultureInfo.InvariantCulture, out var id))
        {
            await RespondAsync("Invalid report ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Obtain the db context.
        using var db = await GetDbContext().ConfigureAwait(false);
        if (await db.ProfileReports.FindAsync(id).ConfigureAwait(false) is not { } report)
        {
            await RespondAsync("Report not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Clear the profile flag
        if (await db.UserProfileData.SingleOrDefaultAsync(p => p.UserUID == report.ReportedUserUID).ConfigureAwait(false) is { } profile)
            profile.FlaggedForReport = false;

        // Remove the report from the database without taking any actions.
        db.Remove(report);
        await db.SaveChangesAsync().ConfigureAwait(false);
        // Then respond.
        await RespondAsync($"Report {id} dismissed.", ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction("report-profile-action-strike-viewing-*")]
    public async Task StrikeProfileViewing(string reportId)
    {
        if (!await ValidateInteraction().ConfigureAwait(false))
            return;

        if (!int.TryParse(reportId, CultureInfo.InvariantCulture, out var id))
        {
            await RespondAsync("Invalid report ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var db = await GetDbContext().ConfigureAwait(false);
        if (await db.ProfileReports.SingleOrDefaultAsync(r => r.ReportID == id).ConfigureAwait(false) is not { } report)
        {
            await RespondAsync("Report no longer exists.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        // Clear the profile flag
        if (await db.UserProfileData.SingleOrDefaultAsync(p => p.UserUID == report.ReportedUserUID).ConfigureAwait(false) is { } profile)
            profile.FlaggedForReport = false;

        var reputation = await LoadTargetReputationAsync(db, report).ConfigureAwait(false);
        await StrikeReputation(reputation, StrikeKind.ProfileViewing).ConfigureAwait(false);

        db.ProfileReports.Remove(report);

        // If now banned, add to banned.
        if (await db.AccountClaimAuth.Include(a => a.User).SingleOrDefaultAsync(a => a.User.UID == reputation.UserUID).ConfigureAwait(false) is { } claim)
            db.BannedRegistrations.Add(new BannedRegistrations() { DiscordId = claim.DiscordId.ToString() });

        await db.SaveChangesAsync().ConfigureAwait(false);

        // Send back to this user they got a strike, along with a forced reconnection.
        await _hubContext.Clients.User(report.ReportedUserUID).SendAsync(nameof(ISundouleiaHub.Callback_HardReconnectMessage),
            MessageSeverity.Warning, "Sundouleia has reviewed a Profile Report on you and given you a strike on Profile Viewing." +
            "Gaining 3 strikes of this catagory will restrict access to view other profiles indefinitely across all profiles." +
            "DM an assistant if you wish to know why.", ServerState.ForcedReconnect).ConfigureAwait(false);

        await RespondAsync($"Profile viewing strike applied to `{report.ReportedUserUID}`.", ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction("report-profile-action-ban-editing-*")]
    public async Task BanProfileEditing(string reportId)
    {
        if (!await ValidateInteraction().ConfigureAwait(false)) return;

        if (!int.TryParse(reportId, CultureInfo.InvariantCulture, out var id))
        {
            await RespondAsync("Invalid report ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var db = await GetDbContext().ConfigureAwait(false);
        if (await db.ProfileReports.SingleOrDefaultAsync(r => r.ReportID == id).ConfigureAwait(false) is not { } report)
        {
            await RespondAsync("Report no longer exists.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (await db.UserProfileData.SingleOrDefaultAsync(p => p.UserUID == report.ReportedUserUID).ConfigureAwait(false) is { } profile)
        {
            profile.Base64AvatarData = string.Empty;
            profile.Description = string.Empty;
            profile.FlaggedForReport = false;
        }


        var reputation = await LoadTargetReputationAsync(db, report).ConfigureAwait(false);
        ApplyImmediateBan(reputation, StrikeKind.ProfileEditing);

        db.ProfileReports.Remove(report);

        // If now banned, add to banned.
        if (await db.AccountClaimAuth.Include(a => a.User).SingleOrDefaultAsync(a => a.User.UID == reputation.UserUID).ConfigureAwait(false) is { } claim)
            db.BannedRegistrations.Add(new BannedRegistrations() { DiscordId = claim.DiscordId.ToString() });

        await db.SaveChangesAsync().ConfigureAwait(false);

        // Send back to this user they got a strike, along with a forced reconnection.
        await _hubContext.Clients.User(report.ReportedUserUID).SendAsync(nameof(ISundouleiaHub.Callback_HardReconnectMessage),
            MessageSeverity.Warning, "Sundouleia has reviewed a Profile Report on you and given you a strike on Profile editing." +
            "Gaining 3 strikes of this catagory will restrict access to view other profiles indefinitely across all profiles." +
            "DM an assistant if you wish to know why.", ServerState.ForcedReconnect).ConfigureAwait(false);

        await RespondAsync($"Profile editing banned for `{report.ReportedUserUID}`.", ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction("report-profile-action-flag-reporter-*")]
    public async Task FlagFalseReporter(string reportId)
    {
        if (!await ValidateInteraction().ConfigureAwait(false)) return;

        if (!int.TryParse(reportId, CultureInfo.InvariantCulture, out var id))
        {
            await RespondAsync("Invalid report ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var db = await GetDbContext().ConfigureAwait(false);
        if (await db.ProfileReports.SingleOrDefaultAsync(r => r.ReportID == id).ConfigureAwait(false) is not { } report)
        {
            await RespondAsync("Report no longer exists.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var reporterRep = await LoadReporterReputationAsync(db, report).ConfigureAwait(false);
        await StrikeReputation(reporterRep, StrikeKind.FalseReport).ConfigureAwait(false);

        db.ProfileReports.Remove(report);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // Send back to this user they got a strike, along with a forced reconnection.
        await _hubContext.Clients.User(report.ReportedUserUID).SendAsync(nameof(ISundouleiaHub.Callback_HardReconnectMessage),
            MessageSeverity.Warning, $"After review, Sundouleia decided your report against another User was falsely made, " +
            $"and a strike has instead been placed on you. DM an assistant if you wish to know why.", ServerState.ForcedReconnect).ConfigureAwait(false);

        await RespondAsync($"Reporter `{report.ReportingUserUID}` flagged for false report.", ephemeral: true).ConfigureAwait(false);
    }


    private async Task<AccountReputation> LoadTargetReputationAsync(SundouleiaDbContext db, ReportedUserProfile report)
    {
        var auth = await db.Auth.SingleAsync(a => a.UserUID == report.ReportedUserUID).ConfigureAwait(false);
        return await db.AccountReputation.SingleAsync(r => r.UserUID == auth.PrimaryUserUID).ConfigureAwait(false);
    }

    private async Task<AccountReputation> LoadReporterReputationAsync(SundouleiaDbContext db, ReportedUserProfile report)
    {
        var auth = await db.Auth.SingleAsync(a => a.UserUID == report.ReportingUserUID).ConfigureAwait(false);
        return await db.AccountReputation.SingleAsync(r => r.UserUID == auth.PrimaryUserUID).ConfigureAwait(false);
    }

    private async Task StrikeReputation(AccountReputation rep, StrikeKind kind)
    {
        var utcNow = DateTime.UtcNow;

        // 1. Increment strikes and determine which bucket we're working with
        int strikeCount = kind switch
        {
            StrikeKind.ProfileViewing => ++rep.ProfileViewStrikes,
            StrikeKind.ProfileEditing => ++rep.ProfileEditStrikes,
            StrikeKind.ChatUsage => ++rep.ChatStrikes,

            StrikeKind.FalseReport =>
                ++rep.FalseReportStrikes,
            _ => 0
        };

        TimeSpan punishmentDuration = strikeCount switch
        {
            1 => TimeSpan.FromHours(12),
            2 => TimeSpan.FromDays(3),
            3 => TimeSpan.FromDays(7),
            4 or 5 => TimeSpan.FromDays(14),
            _ => TimeSpan.Zero
        };

        // 3. Apply punishment
        switch (kind)
        {
            case StrikeKind.ProfileViewing:
                rep.ProfileViewTimeout = utcNow + punishmentDuration;
                if (strikeCount >= 3)
                    rep.ProfileViewing = false;
                break;

            case StrikeKind.ProfileEditing:
                rep.ProfileEditTimeout = utcNow + punishmentDuration;
                if (strikeCount >= 3)
                    rep.ProfileEditing = false;
                break;

            case StrikeKind.ChatUsage:
                rep.ChatTimeout = utcNow + punishmentDuration;
                if (strikeCount >= 3)
                    rep.ChatUsage = false;
                break;

            case StrikeKind.FalseReport:
                // No timeout, just tracking
                break;
        }

        // 4. Auto-ban if global strike threshold hit
        if (rep.ShouldBan)
            rep.IsBanned = true;
    }

    public static void ApplyImmediateBan(AccountReputation rep, StrikeKind type)
    {
        DateTime utcNow = DateTime.UtcNow;

        switch (type)
        {
            case StrikeKind.ProfileViewing:
                rep.ProfileViewing = false;
                rep.ProfileViewTimeout = DateTime.MaxValue;
                rep.ProfileViewStrikes++;
                break;

            case StrikeKind.ProfileEditing:
                rep.ProfileEditing = false;
                rep.ProfileEditTimeout = DateTime.MaxValue;
                rep.ProfileEditStrikes++;
                break;

            case StrikeKind.ChatUsage:
                rep.ChatUsage = false;
                rep.ChatTimeout = DateTime.MaxValue;
                rep.ChatStrikes++;
                break;

            case StrikeKind.ImmidiateBan:
                rep.IsBanned = true;
                rep.ProfileViewing = false;
                rep.ProfileEditing = false;
                rep.ChatUsage = false;
                break;
        }
    }
}