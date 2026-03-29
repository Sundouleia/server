
using Discord;
using Discord.Interactions;
using Discord.Net;
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
using System.Diagnostics;
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
        _ = RunScheduledTask("RemoveDonorPerks", () => RemovePerks(sundGuild, ckGuild, _updateStatusCts.Token), TimeSpan.FromHours(6), _updateStatusCts.Token);
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
        _logger.LogDebug("[AddPerks] Grabbing supporter roles from both discords.");
        var sundouleiaRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        var ckRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        var sundouleiaRoleToId = sundouleiaRoles.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

        // Slow delay while it collects data, but will be much faster after.
        _logger.LogInformation("[AddPerks] Snapshotting Discord guilds...");
        var sw = Stopwatch.StartNew();
        var sundUsers = await SnapshotGuildUsersAsync(sundouleiaGuild, token).ConfigureAwait(false);
        var ckUsers = await SnapshotGuildUsersAsync(ckGuid, token).ConfigureAwait(false);
        sw.Stop();
        _logger.LogInformation($"[AddPerks] Snapshot complete. {sundUsers.Count} users in Sundouleia, {ckUsers.Count} users in CK. (Took: {sw.Elapsed})");
        using var scope = _services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>();
        
        // Obtain all verified sundouleia accounts to narrow our search.
        var verifiedAccounts = await db.AccountReputation.Include(r => r.User)
            .Where(r => r.IsVerified)
            .ToListAsync(token)
            .ConfigureAwait(false);

        var validAuthClaims = await db.AccountClaimAuth.Include(a => a.User)
            .Where(a => a.User != null)
            .ToDictionaryAsync(a => a.User!.UID, token)
            .ConfigureAwait(false);

        _logger.LogDebug($"[AddPerks] Found {verifiedAccounts.Count} verified {sundouleiaGuild.Name} users without perks.");
        // Hold a cache of the users needing updates.
        var usersNeedingDiscordUpdates = new List<(RestGuildUser User, IReadOnlyList<ulong> Roles)>();

        // loop through all users.
        foreach (var account in verifiedAccounts)
        {
            if (!validAuthClaims.TryGetValue(account.UserUID, out var auth))
                continue;

            sundUsers.TryGetValue(auth.DiscordId, out var sundUser);
            ckUsers.TryGetValue(auth.DiscordId, out var ckUser);

            // Check for this condition if the user is in both Sundouleia and in CK.
            var rolesToAssign = new List<ulong>();
            if (sundUser is not null && ckUser is not null)
            {
                // Get their supporter roles in both servers.
                var ckSupporterRoles = ckUser.RoleIds.Where(ckRoles.ContainsKey).ToList();
                // If there were any from them
                if (ckSupporterRoles.Count > 0)
                {
                    // Obtain the highest CK role they have.
                    var highestCkRole = ckSupporterRoles.OrderByDescending(r => RoleToVanityTier[ckRoles[r]]).First();
                    // Check if they have the corresponding role in sundouleia.
                    if (ckRoles.TryGetValue(highestCkRole, out var ckRoleName) && CkToSundRoles.TryGetValue(ckRoleName, out var mappedSundRoles))
                    {
                        // Only add roles that the user does not already have
                        rolesToAssign.AddRange(mappedSundRoles.Where(sundouleiaRoleToId.ContainsKey).Select(r => sundouleiaRoleToId[r]).Where(id => !sundUser.RoleIds.Contains(id)));
                    }
                }
            }

            // Enqueue Ck-Derrived roles for assignment
            if (rolesToAssign.Count > 0)
            {
                _logger.LogInformation($"[AddPerks] User {sundUser?.DisplayName ?? "<UNK>"} ({auth.User!.UID}) is a supporter and missing roles. Assigning [{string.Join(", ", rolesToAssign.Select(id => sundouleiaRoles[id]))}]");
                usersNeedingDiscordUpdates.Add((sundUser!, rolesToAssign));
            }

            // Determine the highest tier from Sundouleia
            if (sundUser is not null)
            {
                // Grab their valid sundouleia roles
                var validSundRoles = sundUser.RoleIds.Where(sundouleiaRoles.ContainsKey).Select(id => RoleToVanityTier[sundouleiaRoles[id]]).ToList();
                if (validSundRoles.Count > 0)
                {
                    // Get the highest tier from their roles
                    var highestSundRole = validSundRoles.Max();
                    // If different from the current, we should update it!
                    if (account.User.Tier != highestSundRole)
                    {
                        _logger.LogInformation($"[AddPerks] Assigning User {auth.User!.UID} ({sundUser.DisplayName}) to tier {highestSundRole} based on Sund roles.");
                        account.User.Tier = highestSundRole;
                        db.Update(account.User);

                        // Update alt accounts
                        var altAccounts = await db.Auth.Include(a => a.User).Where(a => a.PrimaryUserUID == account.UserUID).ToListAsync(token).ConfigureAwait(false);
                        _logger.LogDebug($"[AddPerks] Assigning these perks to {sundUser.DisplayName}'s {altAccounts.Count} alt profiles.");
                        foreach (var alt in altAccounts)
                        {
                            alt.User.Tier = highestSundRole;
                            db.Update(alt.User);
                        }
                    }
                }
            }

            // Save the changes
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }

        // Assign all of the queued roles.
        _logger.LogInformation($"[AddPerks] Assigning roles to {usersNeedingDiscordUpdates.Count} users in Discord...");
        foreach (var (user, roles) in usersNeedingDiscordUpdates)
        {
            await RetryAsync(user.AddRolesAsync(roles), user, "AddDonorRoles").ConfigureAwait(false);
            _logger.LogDebug($"[AddPerks] Assigned roles [{string.Join(", ", roles.Select(id => sundouleiaRoles[id]))}] to {user.DisplayName} ({user.Id})");
        }
    }

    /// <summary> 
    ///     Removes the VanityPerks from users who are no longer supporting CK / Sundouleia <para />
    ///     Note that this is still a WIP as I determine how to cross reference rolls from the other Guild.
    /// </summary>
    private async Task RemovePerks(RestGuild sundouleiaGuild, RestGuild ckGuild, CancellationToken token)
    {
        var appId = await _discordClient.GetApplicationInfoAsync().ConfigureAwait(false);

        _logger.LogInformation($"Cleaning up Vanity UIDs from Sundouleia [{sundouleiaGuild.Name}]");
        var sundouleiaRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        var ckRoles = _discordConfig.GetValueOrDefault(nameof(ServerDiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        var sundouleiaRoleToId = sundouleiaRoles.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

        _logger.LogDebug("[RemovePerks] Snapshotting Discord guilds for perk removal...");
        var sw = Stopwatch.StartNew();
        var sundUsers = await SnapshotGuildUsersAsync(sundouleiaGuild, token).ConfigureAwait(false);
        var ckUsers = await SnapshotGuildUsersAsync(ckGuild, token).ConfigureAwait(false);
        sw.Stop();
        _logger.LogDebug($"[RemovePerks] Snapshot complete. {sundUsers.Count} users in Sundouleia, {ckUsers.Count} users in CK. (Took: {sw.Elapsed})");

        using var scope = _services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>();

        var vanityUsers = await db.AccountClaimAuth.Include(a => a.User)
            .Where(c => c.User != null && c.User.Tier != CkVanityTier.NoRole)
            .ToListAsync()
            .ConfigureAwait(false);

        // This should account for only the main accounts.
        _logger.LogInformation($"[RemovePerks] Processing {vanityUsers.Count} Users...");
        foreach (var authClaim in vanityUsers)
        {
            if (!sundUsers.TryGetValue(authClaim.DiscordId, out var sundUser))
            {
                // Should technically remove roles if not part of the discord i guess and also unverify account?
                _logger.LogWarning($"[RemovePerks] Couldn't find discord user [{authClaim.DiscordId}] in Sundouleia, skipping removal ({authClaim.User!.UID})");
                continue;
            }

            ckUsers.TryGetValue(authClaim.DiscordId, out var ckUser);

            // Roles to remove from Sundouleia user
            var rolesToRemove = new List<ulong>();

            var sundRoleNames = sundUser.RoleIds.Where(sundouleiaRoles.ContainsKey).Select(id => sundouleiaRoles[id]).ToList();
            var hasSundSupporterRoles = sundRoleNames.Any(r => SundouleiaCkSupporters.Contains(r, StringComparer.Ordinal));
            _logger.LogDebug($"[RemovePerks] Checking user {sundUser.DisplayName} ({authClaim.User!.UID}) with roles [{string.Join(", ", sundRoleNames)}]. HasSundSupporterRoles={hasSundSupporterRoles}");
            if (hasSundSupporterRoles)
            {
                var hasCkSupporterRoles = ckUser != null && ckUser.RoleIds.Any(ckRoles.ContainsKey);
                if (!hasCkSupporterRoles)
                {
                    // User is no longer supporting CK, remove the perks
                    rolesToRemove = [.. sundUser.RoleIds.Where(sundouleiaRoles.ContainsKey)];
                    if (rolesToRemove.Count > 0)
                    {
                        _logger.LogInformation($"[RemovePerks] User {authClaim.User!.UID} ({sundUser.DisplayName}) is no longer supporting CK, removing [{string.Join(", ", rolesToRemove.Select(id => sundouleiaRoles[id]))})");
                        await RetryAsync(sundUser.RemoveRolesAsync(rolesToRemove), sundUser, "RemoveRoles").ConfigureAwait(false);

                        // Update their tier and alias in the database.
                        authClaim.User.Tier = CkVanityTier.NoRole;
                        authClaim.User.Alias = string.Empty;
                        db.Update(authClaim.User);

                        // Clear out the alts
                        var secondaryProfiles = await db.Auth.Include(u => u.User)
                            .Where(u => u.PrimaryUserUID == authClaim.User.UID)
                            .ToListAsync(token)
                            .ConfigureAwait(false);

                        foreach (var alt in secondaryProfiles)
                        {
                            _logger.LogDebug($"[RemovePerks] User {alt.UserUID} ({sundUser.DisplayName}) has no allowed roles, clearing this ALT Profile vanity status");
                            alt.User.Tier = CkVanityTier.NoRole;
                            alt.User.Alias = string.Empty;
                            db.Update(alt.User);
                        }
                    }
                }
                else
                {
                    // Still a CK supporter, log that
                    _logger.LogInformation($"[RemovePerks] User {authClaim.User.UID} ({sundUser.DisplayName}) still has CK supporter roles, no action needed. CK Roles: [{string.Join(", ", ckUser!.RoleIds.Where(ckRoles.ContainsKey).Select(id => ckRoles[id]))}]");
                }
            }
            else
            {
                // User has no Sund supporter roles
                _logger.LogDebug($"[RemovePerks] User {authClaim.User.UID} ({sundUser.DisplayName}) has Sundouleia supporter roles but no CK supporter roles, skipping removal.");
            }
        }

        await db.SaveChangesAsync(token).ConfigureAwait(false);
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
            int onlineUsers = await _connectionMultiplexer.GetServer(endPoint).KeysAsync(pattern: "UID:*").CountAsync().ConfigureAwait(false);

            _logger.LogTrace($"Users online: {onlineUsers}");
            await _discordClient.SetActivityAsync(new Game($"with {onlineUsers} Users")).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
    }

    // Get around discords rate limiting the best we can.
    private async Task<Dictionary<ulong, RestGuildUser>> SnapshotGuildUsersAsync(RestGuild guild, CancellationToken token)
    {
        var users = new Dictionary<ulong, RestGuildUser>();
        await foreach (var chunk in guild.GetUsersAsync(new RequestOptions { CancelToken = token }).ConfigureAwait(false))
        {
            _logger.LogDebug($"Fetched chunk of users: {chunk.Count} users in this chunk from guild {guild.Name}");
            foreach (var user in chunk)
                users[user.Id] = user;
        }
        // Log the total number of users fetched.
        _logger.LogInformation($"Total users fetched from {guild.Name}: {users.Count}");
        return users;
    }

    // Get around discords rate limiting the best we can.
    private async Task RetryAsync(Task action, RestGuildUser user, string operation)
    {
        var retryCount = 0;
        var maxRetries = 5;
        var retryDelay = TimeSpan.FromSeconds(5);

        while (retryCount < maxRetries)
        {
            try
            {
                await action.ConfigureAwait(false);
                break;
            }
            catch (RateLimitedException)
            {
                retryCount++;
                _logger.LogWarning($"Rate limited on operation {operation} for user {user.Id}. Retry {retryCount} in {retryDelay}.");
                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{user.Mention} {operation} FAILED: {ex.Message}");
                break;
            }
        }

        if (retryCount == maxRetries)
            _logger.LogWarning($">>> {user.Mention} {operation} FAILED after {maxRetries} retries.");
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