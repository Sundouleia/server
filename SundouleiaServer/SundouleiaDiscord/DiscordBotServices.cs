using Discord;
using Discord.Rest;
using Discord.WebSocket;
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
    private readonly IServiceProvider _services;

    // Discord Server cached Guilds.
    public RestGuild? CkGuildCached;
    public RestGuild? SundouleiaGuildCached; 
    public ConcurrentQueue<KeyValuePair<ulong, Func<DiscordBotServices, Task>>> VerificationQueue { get; } = new(); // the verification queue
    private CancellationTokenSource _verificationTaskCts = new();

    public DiscordBotServices(ILogger<DiscordBotServices> logger, IServiceProvider services)
    {
        Logger = logger;
        _services = services;
    }

    /// <summary>
    ///     Starts the verification process
    /// </summary>
    public Task Start()
    {
        _ = ProcessVerificationQueue();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Stops the verification process
    /// </summary>
    public Task Stop()
    {
        _verificationTaskCts?.Cancel();
        _verificationTaskCts?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Adds a verification task to the queue. (dont think this will have a purpose)
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
            Logger.LogTrace($"Processing Verification Queue, Entries: { VerificationQueue.Count}");
            // if the queue has a peeked item
            if (VerificationQueue.TryPeek(out var queueitem))
            {
                try
                {
                    await queueitem.Value.Invoke(this).ConfigureAwait(false);
                    Logger.LogInformation($"Processed Verification for {queueitem.Key}");
                }
                catch (Exception e)
                {
                    Logger.LogError($"Error during queue work: {e}");
                }
                finally
                {
                    VerificationQueue.TryDequeue(out _);
                }
            }
            // await a delay of 2 seconds
            await Task.Delay(TimeSpan.FromSeconds(2), _verificationTaskCts.Token).ConfigureAwait(false);
        }
    }

    public async Task CreateOrUpdateReportWizardWithExistingMessage(SocketTextChannel channel, IUserMessage message)
    {
        using var scope = _services.CreateAsyncScope();
        using var db = scope.ServiceProvider.GetRequiredService<SundouleiaDbContext>();

        var totalProfileReports = await db.ProfileReports.CountAsync().ConfigureAwait(false);
        var totalRadarReports = await db.RadarReports.CountAsync().ConfigureAwait(false);

        var eb = new EmbedBuilder()
            .WithTitle("Sundouleia Report Wizard")
            .WithDescription("View and decide an outcome for reported chat, radar, and profiles. Select an option below:")
            .WithThumbnailUrl("https://raw.githubusercontent.com/Sundouleia/repo/main/Images/icon.png")
            .AddField("Current Profile Reports", totalProfileReports, true)
            .AddField("Current Radar/Chat Reports", totalRadarReports, true)
            .WithColor(Color.Orange);

        var cb = new ComponentBuilder()
            .WithButton("Profile Reports", "reports-profile-home:true", ButtonStyle.Primary, Emoji.Parse("🖼️"))
            .WithButton("Radar/Chat Reports", "reports-chat-home:true", ButtonStyle.Primary, Emoji.Parse("💬"))
            .WithButton("🔄 Refresh", "reports-refresh", ButtonStyle.Secondary);

        await message.ModifyAsync(m =>
        {
            m.Embed = eb.Build();
            m.Components = cb.Build();
        }).ConfigureAwait(false);
    }

    internal void UpdateCkGuild(RestGuild guild)
    {
        Logger.LogInformation($"Ck Guild Updated to cache: {guild.Name}");
        CkGuildCached = guild;
    }

    internal void UpdateSundouleiaGuild(RestGuild guild)
    {
        Logger.LogInformation($"Sundouleia Guild Updated to cache: {guild.Name}");
        SundouleiaGuildCached = guild;
    }
}

#nullable disable
#pragma warning restore IDISP006 // Implement IDisposable, We already do this in start & stop.