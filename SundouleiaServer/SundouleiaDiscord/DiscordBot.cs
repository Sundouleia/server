
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SundouleiaAPI.Enums;
using SundouleiaDiscord.Commands;
using SundouleiaDiscord.Modules.AccountWizard;
using SundouleiaServer.Hubs;
using SundouleiaShared.Data;
using SundouleiaShared.Services;
using System.Threading.Channels;
using ServerDiscordConfig = SundouleiaShared.Utils.Configuration.DiscordConfig;

namespace SundouleiaDiscord;
#nullable enable

internal partial class DiscordBot : IHostedService
{
    private readonly ILogger<DiscordBot> _logger;
    private readonly IConfigurationService<ServerDiscordConfig> _discordConfig;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDbContextFactory<SundouleiaDbContext> _dbContextFactory;
    private readonly IHubContext<SundouleiaHub> _sundouleiaHubContext;
    private readonly IServiceProvider _services;
    private readonly DiscordBotServices _botServices;
    private readonly DiscordSocketClient _discordClient;

    private InteractionService _interactionModule;
    private CancellationTokenSource _processReportQueueCts = new();
    private CancellationTokenSource _updateStatusCts = new();

    public DiscordBot(
        ILogger<DiscordBot> logger, 
        IConfigurationService<ServerDiscordConfig> discordConfig,
        IConnectionMultiplexer multiplexer,
        IDbContextFactory<SundouleiaDbContext> dbContextFactory,
        IHubContext<SundouleiaHub> hubContext,
        IServiceProvider services,
        DiscordBotServices botServices)
    {
        _logger = logger;
        _discordConfig = discordConfig;
        _connectionMultiplexer = multiplexer;
        _dbContextFactory = dbContextFactory;
        _sundouleiaHubContext = hubContext;
        _services = services;
        _botServices = botServices;

        _discordClient = new(new DiscordSocketConfig() { DefaultRetryMode = RetryMode.AlwaysRetry });
        _interactionModule = new InteractionService(_discordClient);

        // subscribe to the log event from discord.
        _discordClient.Log += Log;
    }

    // Map role names from both servers
    public static readonly Dictionary<string, CkVanityTier> RoleToVanityTier = new Dictionary<string, CkVanityTier>(StringComparer.Ordinal)
    {
        { "Corby", CkVanityTier.ShopKeeper },
        { "Distinguished Connoisseur", CkVanityTier.DistinguishedConnoisseur },
        { "Tier 3 (CK)", CkVanityTier.DistinguishedConnoisseur },
        { "Esteemed Patron", CkVanityTier.EsteemedPatron },
        { "Tier 2 (CK)", CkVanityTier.EsteemedPatron },
        { "Server Booster", CkVanityTier.ServerBooster },
        { "Illustrious Supporter", CkVanityTier.IllustriousSupporter },
        { "Tier 1 (CK)", CkVanityTier.IllustriousSupporter }
    };

    // Map CK role names to all Sundouleia role names they should match
    public static readonly Dictionary<string, string[]> CkToSundRoles = new(StringComparer.Ordinal)
    {
        { "Distinguished Connoisseur", new[] { "Distinguished Connoisseur", "Tier 3 (CK)" } },
        { "Esteemed Patron", new[] { "Esteemed Patron", "Tier 2 (CK)" } },
        { "Illustrious Supporter", new[] { "Illustrious Supporter", "Tier 1 (CK)" } },
    };

    public static readonly IEnumerable<string> SundouleiaCkSupporters = [ "Tier 3 (CK)", "Tier 2 (CK)", "Tier 1 (CK)" ];


    // As this is a hosted service, this fires immediately upon program start.
    public async Task StartAsync(CancellationToken ct)
    {
        // Get our CkBotToken **May need to make a new one for Sundouleia
        string token = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.BotToken), string.Empty);
        if (!string.IsNullOrEmpty(token))
        {
            _logger.LogInformation("Starting DiscordBot");
            // Recreate the new interaction service for this client.
            _interactionModule?.Dispose();
            _interactionModule = new InteractionService(_discordClient);
            _interactionModule.Log += Log;

            // Add Modules for Commands and the account wizard.
            await _interactionModule.AddModuleAsync(typeof(SundouleiaCommands), _services).ConfigureAwait(false);
            await _interactionModule.AddModuleAsync(typeof(AccountWizard), _services).ConfigureAwait(false);
            await _interactionModule.AddModuleAsync(typeof(ReportWizard), _services).ConfigureAwait(false);

            // log the bot into to the discord client with the token
            await _discordClient.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await _discordClient.StartAsync().ConfigureAwait(false);

            // subscribe to the ready event from the discord client
            _discordClient.Ready += DiscordClient_Ready;

            // subscribe to the interaction created event from the discord client
            // (occurs when player interacts with its posted events.)
            _discordClient.InteractionCreated += async (x) =>
            {
                SocketInteractionContext ctx = new SocketInteractionContext(_discordClient, x);
                await _interactionModule.ExecuteCommandAsync(ctx, _services).ConfigureAwait(false);
            };

            await _botServices.Start().ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.BotToken), string.Empty)))
        {
            // await for all bot services to stop
            await _botServices.Stop().ConfigureAwait(false);
            _processReportQueueCts?.Cancel();
            _updateStatusCts?.Cancel();

            await _discordClient.LogoutAsync().ConfigureAwait(false);
            await _discordClient.StopAsync().ConfigureAwait(false);

            // dispose of the discord client
            _discordClient.Dispose();
            _interactionModule?.Dispose();
            _logger.LogInformation("DiscordBot Stopped");
        }
    }

    /// <summary>
    ///     Once the discord client is ready, perform the following.
    /// </summary>
    private async Task DiscordClient_Ready()
    {
        var ckGuildId = _discordConfig.GetValueOrDefault<ulong?>(nameof(ServerDiscordConfig.CkGuildId), 0ul);
        var sundouleiaGuildId = _discordConfig.GetValueOrDefault<ulong?>(nameof(ServerDiscordConfig.SundouleiaGuildId), 0ul);
        // we want to obtain the guilds for Ck and Sundouleia.
        var guilds = await _discordClient.Rest.GetGuildsAsync().ConfigureAwait(false);

        _logger.LogInformation($"Bot found in {guilds.Count} guilds: {string.Join(", ", guilds.Select(g => $"{g.Name} ({g.Id})"))}");
        _logger.LogInformation($"Ck GuildId: {ckGuildId}, Sundouleia GuildId: {sundouleiaGuildId}");
        RestGuild? sundGuild = guilds.FirstOrDefault(g => g.Id == sundouleiaGuildId);
        RestGuild? ckGuild = guilds.FirstOrDefault(g => g.Id == ckGuildId);
        _logger.LogInformation($"Ck Guild: {(ckGuild is null ? "Not Found" : ckGuild.Name)}, Sundouleia Guild: {(sundGuild is null ? "Not Found" : sundGuild.Name)}");

        // Register the commands for the sundouleiaGuid.
        if (sundGuild is not null)
            await _interactionModule.RegisterCommandsToGuildAsync(sundGuild.Id, true).ConfigureAwait(false);
            
        // refresh the update status.
        _updateStatusCts?.Cancel();
        _updateStatusCts?.Dispose();
        _updateStatusCts = new();
        _ = UpdateStatusAsync(_updateStatusCts.Token);

        // Cache CkGuild if present.
        if (ckGuild is not null)
            _botServices.UpdateCkGuild(ckGuild);

        // Create the model for the sundouleia Guild.
        if (sundGuild is not null)
        {
            await CreateOrUpdateAccountWizard(sundGuild).ConfigureAwait(false);
            _botServices.UpdateSundouleiaGuild(sundGuild);
        }

        // If the ckGuild is not null, then we can process vanity perks by merging perks from CK & Sundouleia.
        if (ckGuild is null || sundGuild is null)
            return;

        _ = RunScheduledTask("Process Reports", () => CreateOrUpdateReportWizard(sundGuild), TimeSpan.FromMinutes(30), _updateStatusCts.Token);
        _ = RunScheduledTask("SyncVanityStatus", () => UpdateVanityRoles(sundGuild, ckGuild, _updateStatusCts.Token), TimeSpan.FromHours(12), _updateStatusCts.Token);
        _ = RunScheduledTask("AddDonorPerks", () => AddPerksToVanityUsers(sundGuild, ckGuild, _updateStatusCts.Token), TimeSpan.FromHours(6), _updateStatusCts.Token);
        _ = RunScheduledTask("RemoveDonorPerks", () => RemoveVanityPerks(sundGuild, ckGuild, _updateStatusCts.Token), TimeSpan.FromHours(6), _updateStatusCts.Token);
    }

    private async Task RunScheduledTask(string name, Func<Task> action, TimeSpan interval, CancellationToken cts)
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await action().ConfigureAwait(false);
                await Task.Delay(interval, cts).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break; // Task canceled, exit gracefully
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in repeating task {TaskName}", name);
            }
        }
    } 

    /// <summary>
    ///     The primary function for creating / updating the account claim system
    /// </summary>
    private async Task CreateOrUpdateAccountWizard(RestGuild guild)
    {
        _logger.LogDebug("Account Wizard: Getting Channel");
        ulong? commandsChannel = _discordConfig.GetValue<ulong?>(nameof(ServerDiscordConfig.ChannelForCommands));
        if (commandsChannel is null)
        {
            _logger.LogWarning("Account Wizard: No channel configured");
            return;
        }

        // create the message
        IUserMessage? message = null;
        var socketChannel = await _discordClient.GetChannelAsync(commandsChannel.Value).ConfigureAwait(false) as SocketTextChannel ?? throw new Exception("Channel not found");

        IReadOnlyCollection<RestMessage> pinnedMessages = await socketChannel.GetPinnedMessagesAsync().ConfigureAwait(false);
        foreach (RestMessage msg in pinnedMessages)
        {
            _logger.LogDebug($"Account Wizard: Checking message {msg.Id} | Author: {msg.Author.Id} | HasEmbeds: {msg.Embeds.Any()}");
            if (msg.Author.Id == _discordClient.CurrentUser.Id && msg.Embeds.Any())
            {
                message = await socketChannel.GetMessageAsync(msg.Id).ConfigureAwait(false) as IUserMessage;
                break;
            }
        }

        _logger.LogInformation($"Account Wizard: Found message id: {(message?.Id ?? 0)}");
        // construct the embed builder
        var eb = new EmbedBuilder()
            .WithTitle("Sundouleia Account Service")
            .WithDescription("To Register or manage your account, use the button below!")
            .WithColor(Color.Gold)
            .WithThumbnailUrl("https://raw.githubusercontent.com/Sundouleia/repo/main/Images/icon.png");
        // construct the buttons
        var cb = new ComponentBuilder()
            .WithButton("Open Account Management", style: ButtonStyle.Primary, customId: "wizard-home:true", emote: Emoji.Parse("🗝️"));
        // if the previous message is null, then send the message and try and pin it.
        if (message is null)
        {
            var msg = await socketChannel.SendMessageAsync(embed: eb.Build(), components: cb.Build()).ConfigureAwait(false);
            try
            {
                await msg.PinAsync().ConfigureAwait(false);
            }
            catch (Exception) { /* Blep */ }
        }
        else // if message is already generated, just modify it.
        {
            await message.ModifyAsync(p =>
            {
                p.Embed = eb.Build();
                p.Components = cb.Build();
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The primary function for creating / updating the report system
    /// </summary>
    private async Task CreateOrUpdateReportWizard(RestGuild guild)
    {
        _logger.LogDebug("Report Wizard: Getting Channel");

        ulong? reportsChannel = _discordConfig.GetValue<ulong?>(nameof(ServerDiscordConfig.ChannelForReports));
        if (reportsChannel is null)
        {
            _logger.LogWarning("Report Wizard: No channel configured");
            return;
        }

        var channel = await _discordClient.GetChannelAsync(reportsChannel.Value).ConfigureAwait(false) as SocketTextChannel
            ?? throw new Exception("Channel not found");

        IUserMessage? message = null;
        var pinnedMessages = await channel.GetPinnedMessagesAsync().ConfigureAwait(false);
        foreach (var msg in pinnedMessages)
        {
            if (msg.Author.Id == _discordClient.CurrentUser.Id && msg.Embeds.Any())
            {
                message = await channel.GetMessageAsync(msg.Id).ConfigureAwait(false) as IUserMessage;
                break;
            }
        }

        // Clean up the messages in the channel that are not pinned messages.
        var toCleanup = await channel.GetMessagesAsync(100).FlattenAsync().ConfigureAwait(false);
        var deletable = toCleanup
            .Where(m => m.Id != message?.Id && (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14)
            .ToList();
        // Attempt cleanup.
        try
        {
            if (deletable.Count == 1)
                await deletable[0].DeleteAsync().ConfigureAwait(false);
            else
                await channel.DeleteMessagesAsync(deletable).ConfigureAwait(false);
        }
        catch
        { }

        _logger.LogInformation($"Report Wizard: Found message id: {message?.Id ?? 0}");
        using var scope = _services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>();

        var totalProfileReports = await db.ProfileReports.CountAsync().ConfigureAwait(false);
        var totalChatReports = await db.RadarReports.CountAsync().ConfigureAwait(false);

        var eb = new EmbedBuilder()
            .WithTitle("Sundouleia Report Wizard")
            .WithDescription("View and decide an outcome for reported chat and profiles. Select an option below:")
            .WithThumbnailUrl("https://raw.githubusercontent.com/CordeliaMist/GagSpeak-Client/main/images/iconUI.png")
            .AddField("Current Profile Reports", totalProfileReports, true)
            .AddField("Current Radar/Chat Reports", totalChatReports, true)
            .WithColor(Color.Orange);
        // construct the buttons
        var cb = new ComponentBuilder()
            .WithButton("Profile Reports", "reports-profile-home:true", ButtonStyle.Primary, Emoji.Parse("🖼️"))
            .WithButton("Radar/Chat Reports", "reports-chat-home:true", ButtonStyle.Primary, Emoji.Parse("💬"))
            .WithButton("🔄 Refresh", "reports-refresh", ButtonStyle.Secondary);

        // if the previous message is null
        if (message is null)
        {
            var msg = await channel.SendMessageAsync(embed: eb.Build(), components: cb.Build()).ConfigureAwait(false);
            try
            {
                await msg.PinAsync().ConfigureAwait(false);
            }
            catch (Exception)
            { }
        }
        else
        {
            await message.ModifyAsync(p =>
            {
                p.Embed = eb.Build();
                p.Components = cb.Build();
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Updates the vanity roles in the concurrent dictionary for the bot services to reflect the list in the appsettings.json
    /// </summary>
    private async Task UpdateVanityRoles(RestGuild sundGuild, RestGuild ckGuild, CancellationToken token)
    {
        _logger.LogInformation("Updating Sundouleia VanityRoles From Config");
        Dictionary<ulong, string> vanityRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        // if the vanity roles are not the same as the list fetched from the bot service,
        if (vanityRoles.Keys.Count != _botServices.VanityRoles.Count)
        {
            // clear the roles in the bot service, and create a new list with the correct roles.
            _botServices.VanityRoles.Clear();
            foreach (KeyValuePair<ulong, string> role in vanityRoles)
            {
                if (sundGuild.GetRole(role.Key) is { } restRole)
                {
                    _botServices.VanityRoles.Add(restRole, role.Value);
                    _logger.LogDebug($"Adding Sundouleia Role: {role.Key} => {role.Value}");
                }
            }
        }

        _logger.LogInformation("Updating CK VanityRoles From Config");
        Dictionary<ulong, string> ckVanityRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        // if the vanity roles are not the same as the list fetched from the bot service,
        if (ckVanityRoles.Keys.Count != _botServices.CkVanityRoles.Count)
        {
            // clear the roles in the bot service, and create a new list with the correct roles.
            _botServices.CkVanityRoles.Clear();
            foreach (KeyValuePair<ulong, string> role in ckVanityRoles)
            {
                if (ckGuild.GetRole(role.Key) is { } restRole)
                {
                    _botServices.CkVanityRoles.Add(restRole, role.Value);
                    _logger.LogDebug($"Adding CK Role: {role.Key} => {role.Value}");
                }
            }
        }
    }

    /// <summary>
    ///     Takes any any verified claimed account with sundouleia, checks against their roles, and adds perks accordingly. <para />
    ///     This also will apply any missing roles to users with perks in the CkGuid prior to operation.
    /// </summary>
    private async Task AddPerksToVanityUsers(RestGuild sundouleiaGuild, RestGuild ckGuid, CancellationToken token)
    {
        _logger.LogDebug("Grabbing supporter roles from both discords.");
        var sundouleiaRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        var ckRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        var sundouleiaRoleToId = sundouleiaRoles.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

        // This is heavily unoptimized, preferably run 2 passes to collect all users in both discords, then iterate over the data and set accordingly.
        using var scope = _services.CreateAsyncScope();
        using (var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>())
        {
            // Obtain all verified sundouleia accounts to narrow our search.
            var verifiedUsersWithoutPerks = await db.AccountReputation.Include(r => r.User)
                .Where(r => r.IsVerified && r.User.Tier == CkVanityTier.NoRole)
                .ToListAsync()
                .ConfigureAwait(false);

            _logger.LogDebug($"Found {verifiedUsersWithoutPerks.Count} verified users without perks.");

            // Run a loop to check each of these users with some delay to avoid rate limiting.
            // For each check, ensure they have a valid accountClaimAuth.
            foreach (var verifiedAccount in verifiedUsersWithoutPerks)
            {
                // Ensure they have a valid auth claim (should be unnecessary but never know.
                try
                {
                    if (await db.AccountClaimAuth.AsNoTracking().Include(a => a.User).SingleOrDefaultAsync(a => a.User != null && a.User.UID == verifiedAccount.UserUID).ConfigureAwait(false) is not { } authClaim)
                    {
                        _logger.LogWarning($"Somehow was a verified account without an auth claim? Report this if seen!");
                        continue;
                    }

                    // grab the discord user from sundouleia.
                    if (await sundouleiaGuild.GetUserAsync(authClaim.DiscordId).ConfigureAwait(false) is not { } sundouleiaUser)
                    {
                        _logger.LogWarning($"Could not find discord user [{authClaim.DiscordId}] in Sundouleia guild.");
                        await Task.Delay(250, token).ConfigureAwait(false);
                        continue;
                    }

                    // If they do not contain any roles, check if they have any roles in ck first, and if they do, assign them.
                    if (!sundouleiaUser.RoleIds.Any(sundouleiaRoles.Keys.Contains))
                    {
                        // Scope into ck and check their user there.
                        if (await ckGuid.GetUserAsync(authClaim.DiscordId).ConfigureAwait(false) is { } ckUser)
                        {
                            // get their roleIds.
                            var roleIds = ckUser.RoleIds;
                            _logger.LogInformation($"User {authClaim.User!.UID} has CK Roles: {string.Join(", ", roleIds)}");
                            // Determine the highest role to check against by it's priority.
                            var highestRoleId = roleIds.Where(ckRoles.ContainsKey).OrderByDescending(id => RoleToVanityTier[ckRoles[id]]).FirstOrDefault();
                            // Get the string name of this role.
                            if (ckRoles.TryGetValue(highestRoleId, out var priorityRole) && CkToSundRoles.TryGetValue(priorityRole, out var matchingSundRoles))
                            {
                                // Assign all matching sundouleiaRoles to the user.
                                var rolesToAssign = matchingSundRoles.Select(r => sundouleiaRoleToId[r]).ToList();
                                await sundouleiaUser.AddRolesAsync(rolesToAssign).ConfigureAwait(false);
                                _logger.LogInformation($"Assigned Roles from Ck Supporter {authClaim.User!.UID} assigned roles: {string.Join(", ", matchingSundRoles)}");
                            }
                        }
                    }

                    // Now we should check their roles in sundouleia, and assign the highest role they have.
                    if (sundouleiaUser.RoleIds.Any(sundouleiaRoles.Keys.Contains))
                    {
                        // fetch the roles they have, and output them.
                        _logger.LogInformation($"User {authClaim.User!.UID} has roles: {string.Join(", ", sundouleiaUser.RoleIds)}");
                        // Determine the highest priority role
                        CkVanityTier highestRole = sundouleiaUser.RoleIds
                            .Where(sundouleiaRoles.ContainsKey)
                            .Select(id => RoleToVanityTier[sundouleiaRoles[id]])
                            .OrderByDescending(tier => tier)
                            .FirstOrDefault();

                        // Assign Highest role found.
                        verifiedAccount.User.Tier = highestRole;
                        db.Update(verifiedAccount.User);
                        _logger.LogInformation($"User {authClaim.User.UID} assigned to tier {highestRole} (highest role)");

                        // Update this on all secondary accounts of this user.
                        var altProfiles = await db.Auth.Include(a => a.User).Where(a => a.PrimaryUserUID == authClaim.User.UID).ToListAsync().ConfigureAwait(false);
                        foreach (var profile in altProfiles)
                        {
                            _logger.LogDebug($"AltProfile [{profile.User.UID}] also given this perk!");
                            profile.User.Tier = highestRole;
                            db.Update(profile.User);
                        }
                    }
                    // await for the database to save changes
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing user for perks: {ex}");
                }
                // await a second before checking the next user
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary> 
    ///     Removes the VanityPerks from users who are no longer supporting CK / Sundouleia <para />
    ///     Note that this is still a WIP as I determine how to cross reference rolls from the other Guild.
    /// </summary>
    private async Task RemoveVanityPerks(RestGuild sundouleiaGuild, RestGuild ckGuid, CancellationToken token)
    {
        var appId = await _discordClient.GetApplicationInfoAsync().ConfigureAwait(false);
        _logger.LogInformation($"Cleaning up Vanity UIDs from Sundouleia [{sundouleiaGuild.Name}]");
        Dictionary<ulong, string> sundouleiaRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        Dictionary<ulong, string> ckRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        var sundouleiaRoleToId = sundouleiaRoles.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

        using var scope = _services.CreateAsyncScope();
        using (var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>())
        {
            var vanityUsers = await db.AccountClaimAuth.Include(a => a.User)
                .Where(c => c.User != null && c.User.Tier != CkVanityTier.NoRole)
                .ToListAsync()
                .ConfigureAwait(false);

            // This should account for only the main accounts.
            foreach (var authClaim in vanityUsers)
            {
                try
                {
                    // grab the discord user from sundouleia.
                    if (await sundouleiaGuild.GetUserAsync(authClaim.DiscordId).ConfigureAwait(false) is not { } sundouleiaUser)
                    {
                        _logger.LogWarning($"Could not find discord user [{authClaim.DiscordId}] in Sundouleia guild.");
                        await Task.Delay(250, token).ConfigureAwait(false);
                        continue;
                    }

                    // If any of their roles have a matched value contained in the SundouleiaCkSupporters list, we should validate them.
                    if (sundouleiaUser.RoleIds.Any(r => sundouleiaRoles.TryGetValue(r, out var label) && SundouleiaCkSupporters.Contains(label, StringComparer.Ordinal)))
                    {
                        // They have a ck supporter role in sundouleia, so we should validate their roles in ck.
                        if (await ckGuid.GetUserAsync(authClaim.DiscordId).ConfigureAwait(false) is { } ckUser)
                        {
                            // If none of the ckUser's roleIds are in the dictionary, then we should remove all perks in sundouleia.
                            if (!ckUser.RoleIds.Any(ckRoles.Keys.Contains))
                            {
                                _logger.LogInformation($"User {authClaim.User!.UID} no longer has CK supporter roles, removing perks.");
                                var rolesToRemove = sundouleiaUser.RoleIds.Where(sundouleiaRoles.Keys.Contains);
                                if (rolesToRemove.Any())
                                    await sundouleiaUser.RemoveRolesAsync(rolesToRemove).ConfigureAwait(false);
                            }
                        }
                    }
                    // Handle normally.
                    else
                    {
                        if (!sundouleiaUser.RoleIds.Any(sundouleiaRoles.Keys.Contains))
                        {
                            _logger.LogInformation($"User {authClaim.User!.UID} not in allowed roles, deleting alias");
                            authClaim.User.Alias = string.Empty;
                            authClaim.User.Tier = CkVanityTier.NoRole;
                            db.Update(authClaim.User);
                            // Clear out the vanity perks from their alts.
                            var secondaryUsers = await db.Auth.Include(u => u.User).Where(u => u.PrimaryUserUID == authClaim.User.UID).ToListAsync().ConfigureAwait(false);
                            foreach (var secondaryUser in secondaryUsers)
                            {
                                _logger.LogDebug($"Secondary User {secondaryUser!.User!.UID} not in allowed roles, deleting alias & resetting supporter tier");
                                secondaryUser.User.Alias = string.Empty;
                                secondaryUser.User.Tier = CkVanityTier.NoRole;
                                db.Update(secondaryUser.User);
                            }
                        }
                    }
                    // await for the database to save changes
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing user for perk removal: {ex}");
                }
                // await a second before checking the next user
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Updates the status of the bot at the interval
    /// </summary>
    private async Task UpdateStatusAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            System.Net.EndPoint endPoint = _connectionMultiplexer.GetEndPoints().First();
            // fetch the total number of online users connected to the redis server
            int onlineUsers = await _connectionMultiplexer.GetServer(endPoint).KeysAsync(pattern: "SundouleiaHub:UID:*").CountAsync().ConfigureAwait(false);

            _logger.LogTrace($"Users online: {onlineUsers}");
            await _discordClient.SetActivityAsync(new Game($"with {onlineUsers} Users")).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Translate discords log messages into our logger format
    /// </summary>
    private Task Log(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                _logger.LogError(msg.Exception, msg.Message); break;
            case LogSeverity.Warning:
                _logger.LogWarning(msg.Exception, msg.Message); break;
            default:
                _logger.LogInformation(msg.Message); break;
        }

        return Task.CompletedTask;
    }

}
#nullable disable