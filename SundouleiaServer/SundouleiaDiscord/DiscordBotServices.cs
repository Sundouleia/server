using Discord;
using Discord.Rest;
using Microsoft.EntityFrameworkCore;
using SundouleiaShared.Data;
using SundouleiaShared.Services;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DiscordConfig = SundouleiaShared.Utils.Configuration.DiscordConfig;

// namespace dedicated to the discord bot.
namespace SundouleiaDiscord;
#nullable enable
#pragma warning disable IDISP006

// the class overviewing the bots services.
public class DiscordBotServices
{
    // for mapping the initial generated keys provided to the discord user who input it.
    public ConcurrentDictionary<ulong, (string, string)> DiscordInitialKeyMapping = new();
    // the same as above, but for relinking process.
    public ConcurrentDictionary<ulong, (string, string)> DiscordRelinkInitialKeyMapping = new();

    // a concurrent dictionary of the discord users who have verified their Sundouleia account.
    public ConcurrentDictionary<ulong, bool> DiscordVerifiedUsers { get; } = new();

    // the concurrent dictionary representing the last time a user has interacted with the bot.
    public ConcurrentDictionary<ulong, ulong> ValidInteractions { get; } = new();

    // the various vanity roles that the bot can assign to users.
    public Dictionary<RestRole, string> VanityRoles { get; set; } = new();
    public Dictionary<RestRole, string> CkVanityRoles { get; set; } = new();

    public ILogger<DiscordBotServices> Logger { get; init; }
    private readonly IConfigurationService<DiscordConfig> _config;
    private readonly IServiceProvider _services;

    // Discord Server cached Guilds.
    public RestGuild? CkGuildCached;
    public RestGuild? SundouleiaGuildCached; 
    public ConcurrentQueue<KeyValuePair<ulong, Func<DiscordBotServices, Task>>> VerificationQueue { get; } = new(); // the verification queue
    private CancellationTokenSource _verificationTaskCts = new();

    public DiscordBotServices(ILogger<DiscordBotServices> logger, IServiceProvider services,
        IConfigurationService<DiscordConfig> config)
    {
        Logger = logger;
        _config = config;
        _services = services;
    }

    /// <summary> Starts the verification process </summary>
    public Task Start()
    {
        _ = ProcessVerificationQueue();
        return Task.CompletedTask;
    }

    /// <summary> Stops the verification process </summary>
    public Task Stop()
    {
        _verificationTaskCts?.Cancel();
        _verificationTaskCts?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a verification task to the queue. (dont think this will have a purpose)
    /// </summary>
    private async Task ProcessVerificationQueue()
    {
        // create a new cts for the verification task
        _verificationTaskCts?.Cancel();
        _verificationTaskCts?.Dispose();
        _verificationTaskCts = new CancellationTokenSource();
        // while the cancellation token is not requested
        while (!_verificationTaskCts.IsCancellationRequested)
        {
            // log the debug message that we are processing the verification queue
            Logger.LogTrace("Processing Verification Queue, Entries: {entr}", VerificationQueue.Count);
            // if the queue has a peeked item
            if (VerificationQueue.TryPeek(out var queueitem))
            {
                // try and
                try
                {
                    // invoke the queue item and await the result
                    await queueitem.Value.Invoke(this).ConfigureAwait(false);
                    // log the information that the verification has been processed
                    Logger.LogInformation("Processed Verification for {key}", queueitem.Key);
                }
                catch (Exception e)
                {
                    // log the error that occured during the queue work
                    Logger.LogError(e, "Error during queue work");
                }
                finally
                {
                    // finally we should dequeue the item regardless of the outcome
                    VerificationQueue.TryDequeue(out _);
                }
            }

            // await a delay of 2 seconds
            await Task.Delay(TimeSpan.FromSeconds(2), _verificationTaskCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Very messy, refactor later.
    /// </summary>
    public async Task ProcessReports(IUser discordUser, CancellationToken token)
    {
        // if the guild is null, log the warning that the guild is null and return
        if (SundouleiaGuildCached is null)
        {
            Logger.LogWarning("No Guild Cached");
            return;
        }
        // if user id is null, log the warning that the user id is null and return
        Logger.LogInformation($"Processing Reports Queue for Guild {SundouleiaGuildCached.Name} from User: {discordUser.GlobalName}");

        // otherwise grab our channel report ID
        var reportChannelId = _config.GetValue<ulong?>(nameof(DiscordConfig.ChannelForReports));
        if (reportChannelId is null)
        {
            Logger.LogWarning("No report channel configured");
            return;
        }

        var restChannel = await SundouleiaGuildCached.GetTextChannelAsync(reportChannelId.Value).ConfigureAwait(false);
        // Filter messages to only delete profile report messages sent by the bot
        var messages = await restChannel.GetMessagesAsync().FlattenAsync().ConfigureAwait(false);
        var profileReportMessages = messages.Where(m => m.Author.Id == discordUser.Id);

        // Further filter messages to exclude those that contain an embed with a header labeled "Resolution"
        var messagesToDelete = profileReportMessages
            .Where(m => !m.Embeds.Any(e => e.Fields.Any(f => f.Name.Equals("Resolution", StringComparison.OrdinalIgnoreCase))))
            .Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays <= 14); // Only include messages younger than two weeks


        // Delete messages
        if (messagesToDelete.Any())
        {
            await restChannel.DeleteMessagesAsync(messagesToDelete).ConfigureAwait(false);
        }

        try
        {
            // within the scope of the service provider, execute actions using the Sundouleia DbContext
            using (var scope = _services.CreateScope())
            {
                Logger.LogInformation("Checking for Profile Reports");
                var dbContext = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>();
                if (!dbContext.ProfileReports.Any()) {
                    Logger.LogInformation("No Profile Reports Found");
                    return;
                }

                // collect the list of profile reports otherwise and get the report channel
                var reports = await dbContext.ProfileReports.ToListAsync().ConfigureAwait(false);
                Logger.LogInformation($"Found {reports.Count} Reports");

                // for each report, generate an embed and send it to the report channel
                foreach (var report in reports)
                {
                    Logger.LogDebug($"Displaying Report for {report.ReportedUserUID} by {report.ReportingUserUID}");
                    // get the user who reported
                    var reportedUser = await dbContext.Users.SingleAsync(u => u.UID == report.ReportedUserUID).ConfigureAwait(false);
                    var reportedUserAccountClaim = await dbContext.AccountClaimAuth.SingleOrDefaultAsync(u => u!.User!.UID == report.ReportedUserUID).ConfigureAwait(false);

                    // get the user who was reported
                    var reportingUser = await dbContext.Users.SingleAsync(u => u.UID == report.ReportingUserUID).ConfigureAwait(false);
                    var reportingUserAccountClaim = await dbContext.AccountClaimAuth.SingleOrDefaultAsync(u => u!.User!.UID == report.ReportingUserUID).ConfigureAwait(false);

                    // get the profile data of the reported user.
                    var reportedUserProfile = await dbContext.UserProfileData.SingleAsync(u => u.UserUID == report.ReportedUserUID).ConfigureAwait(false);


                    // create an embed post to display reported profiles.
                    EmbedBuilder eb = new();
                    eb.WithTitle("Sundouleia Profile Report");

                    StringBuilder reportedUserSb = new();
                    StringBuilder reportingUserSb = new();
                    reportedUserSb.Append(reportedUser.UID);
                    reportingUserSb.Append(reportingUser.UID);
                    if (reportedUserAccountClaim != null)
                    {
                        reportedUserSb.AppendLine($" (<@{reportedUserAccountClaim.DiscordId}>)");
                    }
                    if (reportingUserAccountClaim != null)
                    {
                        reportingUserSb.AppendLine($" (<@{reportingUserAccountClaim.DiscordId}>)");
                    }
                    eb.AddField("Report Initiator", reportingUserSb.ToString());
                    var reportTimeUtc = new DateTimeOffset(report.ReportTime, TimeSpan.Zero);
                    var formattedTimestamp = string.Create(CultureInfo.InvariantCulture, $"<t:{reportTimeUtc.ToUnixTimeSeconds()}:F>");
                    eb.AddField("Report Time (Local)", formattedTimestamp);
                    eb.AddField("Report Reason", string.IsNullOrWhiteSpace(report.ReportReason) ? "-" : report.ReportReason);

                    // main report:
                    eb.AddField("Reported User", reportedUserSb.ToString());
                    eb.AddField("Reported User Profile Description", string.IsNullOrWhiteSpace(report.ReportReason) ? "-" : report.ReportReason);

                    var cb = new ComponentBuilder();
                    cb.WithButton("Dismiss Report", customId: $"sundouleia-report-button-dismissreport-{reportedUser.UID}", style: ButtonStyle.Primary);
                    cb.WithButton("Clear Profile", customId: $"sundouleia-report-button-clearprofileimage-{reportedUser.UID}", style: ButtonStyle.Secondary);
                    cb.WithButton("Revoke Social Features", customId: $"sundouleia-report-button-revokesocialfeatures-{reportedUser.UID}", style: ButtonStyle.Secondary);
                    cb.WithButton("Ban User", customId: $"sundouleia-report-button-banuser-{reportedUser.UID}", style: ButtonStyle.Danger);
                    cb.WithButton("Dismiss & Flag Reporting User", customId: $"sundouleia-report-button-flagreporter-{reportedUser.UID}-{reportingUser.UID}", style: ButtonStyle.Danger);

                    // Create a list for FileAttachments
                    var attachments = new List<FileAttachment>();


                    // List to keep track of streams to dispose later
                    var streamsToDispose = new List<MemoryStream>();

                    try
                    {
                        // Conditionally add the reported image
                        if (!string.IsNullOrEmpty(report.SnapshotImage))
                        {
                            var reportedImageFileName = reportedUser.UID + "_profile_reported_" + Guid.NewGuid().ToString("N") + ".png";
                            var reportedImageStream = new MemoryStream(Convert.FromBase64String(report.SnapshotImage));
                            streamsToDispose.Add(reportedImageStream);
                            var reportedImageAttachment = new FileAttachment(reportedImageStream, reportedImageFileName);
                            attachments.Add(reportedImageAttachment);
                            eb.WithImageUrl($"attachment://{reportedImageFileName}");
                        }

                        // Send files if there are any attachments
                        if (attachments.Count > 0)
                        {
                            await restChannel.SendFilesAsync(attachments, embed: eb.Build(), components: cb.Build()).ConfigureAwait(false);
                        }
                        else
                        {
                            // If no attachments, send the message with only the embed and components
                            await restChannel.SendMessageAsync(embed: eb.Build(), components: cb.Build()).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        // Dispose of all streams
                        foreach (var stream in streamsToDispose)
                            stream.Dispose();
                    }
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);

                // Now handle reports
                var radarReports = await dbContext.RadarReports.ToListAsync().ConfigureAwait(false);
                Logger.LogInformation($"Found {reports.Count} Reports");

                // for each report, generate an embed and send it to the report channel
                foreach (var radarReport in radarReports)
                {
                    Logger.LogDebug($"Displaying Report for {radarReport.ReportedUserUID} by {radarReport.ReporterUID}");
                    // get the user who reported
                    var reportedUser = await dbContext.Users.SingleAsync(u => u.UID == radarReport.ReportedUserUID).ConfigureAwait(false);
                    var reportedUserAccountClaim = await dbContext.AccountClaimAuth.SingleOrDefaultAsync(u => u!.User!.UID == radarReport.ReportedUserUID).ConfigureAwait(false);

                    // get the user who was reported
                    var reportingUser = await dbContext.Users.SingleAsync(u => u.UID == radarReport.ReporterUID).ConfigureAwait(false);
                    var reportingUserAccountClaim = await dbContext.AccountClaimAuth.SingleOrDefaultAsync(u => u!.User!.UID == radarReport.ReporterUID).ConfigureAwait(false);

                    // get the profile data of the reported user.
                    var reportedUserProfile = await dbContext.UserProfileData.SingleAsync(u => u.UserUID == radarReport.ReportedUserUID).ConfigureAwait(false);

                    // create an embed post to display reported profiles.
                    EmbedBuilder eb = new();
                    eb.WithTitle("Sundouleia Radar Report");

                    StringBuilder reportedUserSb = new();
                    StringBuilder reportingUserSb = new();
                    reportedUserSb.Append(reportedUser.UID);
                    reportingUserSb.Append(reportingUser.UID);
                    if (reportedUserAccountClaim != null)
                    {
                        reportedUserSb.AppendLine($" (<@{reportedUserAccountClaim.DiscordId}>)");
                    }
                    if (reportingUserAccountClaim != null)
                    {
                        reportingUserSb.AppendLine($" (<@{reportingUserAccountClaim.DiscordId}>)");
                    }
                    eb.AddField("Report Initiator", reportingUserSb.ToString());
                    var reportTimeUtc = new DateTimeOffset(radarReport.ReportTime, TimeSpan.Zero);
                    var formattedTimestamp = string.Create(CultureInfo.InvariantCulture, $"<t:{reportTimeUtc.ToUnixTimeSeconds()}:F>");
                    eb.AddField("Report Time (Local)", formattedTimestamp);
                    eb.AddField("Report Reason", string.IsNullOrWhiteSpace(radarReport.ReportReason) ? "-" : radarReport.ReportReason);
                    eb.AddField("Reported User", reportedUserSb.ToString());

                    var cb = new ComponentBuilder();
                    // For now report results will affect the user universally, we will need to manually decide on bans until this is refactored.
                    cb.WithButton("Dismiss Report", customId: $"sundouleia-report-button-dismissreport-{reportedUser.UID}", style: ButtonStyle.Primary);
                    cb.WithButton("Clear Profile", customId: $"sundouleia-report-button-clearprofileimage-{reportedUser.UID}", style: ButtonStyle.Secondary);
                    cb.WithButton("Revoke Social Features", customId: $"sundouleia-report-button-revokesocialfeatures-{reportedUser.UID}", style: ButtonStyle.Secondary);
                    cb.WithButton("Ban User", customId: $"sundouleia-report-button-banuser-{reportedUser.UID}", style: ButtonStyle.Danger);
                    cb.WithButton("Dismiss & Flag Reporting User", customId: $"sundouleia-report-button-flagreporter-{reportedUser.UID}-{reportingUser.UID}", style: ButtonStyle.Danger);

                    // If no attachments, send the message with only the embed and components
                    await restChannel.SendMessageAsync(embed: eb.Build(), components: cb.Build()).ConfigureAwait(false);
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);

            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to process reports");
        }
    }

    internal void UpdateCkGuild(RestGuild guild)
    {
        Logger.LogInformation("Ck Guild Updated to cache: "+ guild.Name);
        CkGuildCached = guild;
    }

    internal void UpdateSundouleiaGuild(RestGuild guild)
    {
        Logger.LogInformation("Sundouleia Guild Updated to cache: " + guild.Name);
        SundouleiaGuildCached = guild;
    }
}

#nullable disable
#pragma warning restore IDISP006 // Implement IDisposable, We already do this in start & stop.