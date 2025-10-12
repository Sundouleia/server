using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using SundouleiaShared.Data;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;
using System.Globalization;

namespace SundouleiaDiscord.Modules.AccountWizard;

// NOTICE: This file has no current use, and was derived from mare's discord bot framework.
// Because Sundouleia embeds its profiles directly and does not allow the modification of keys, this serves no current purpose.
// Keep it here however, in the case we may eventually need to use this one day.
public partial class AccountWizard
{
    [ComponentInteraction("wizard-recover")]
    public async Task ComponentRecover()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRecover), Context.Interaction.User.Id);

        using var sundouleiaDb = await GetDbContext().ConfigureAwait(false);
        EmbedBuilder eb = new();
        eb.WithColor(Color.Gold);
        eb.WithTitle("Recover");
        eb.WithDescription("In case you have lost your secret key you can recover it here." + Environment.NewLine + Environment.NewLine
            + "## ⚠️ **Once you recover your key, the previously used key will be invalidated. If you use Sundouleia on multiple devices you will have to update the key everywhere you use it.** ⚠️" + Environment.NewLine + Environment.NewLine
            + "Use the selection below to select the user account you want to recover." + Environment.NewLine + Environment.NewLine
            + "- 1️⃣ is your primary account/UID" + Environment.NewLine
            + "- 2️⃣ are all your secondary accounts/UIDs" + Environment.NewLine
            + "If you are using Vanity UIDs the original UID is displayed in the second line of the account selection.");
        ComponentBuilder cb = new();
        await AddUserSelection(sundouleiaDb, cb, "wizard-recover-select").ConfigureAwait(false);
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-recover-select")]
    public async Task SelectionRecovery(string uid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(SelectionRecovery), Context.Interaction.User.Id, uid);

        using var sundouleiaDb = await GetDbContext().ConfigureAwait(false);
        EmbedBuilder eb = new();
        eb.WithColor(Color.Green);
        await HandleRecovery(sundouleiaDb, eb, uid).ConfigureAwait(false);
        ComponentBuilder cb = new();
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    private async Task HandleRecovery(SundouleiaDbContext db, EmbedBuilder embed, string uid)
    {
        // Fetch the previous auth we want to recover. We should then replace that auth with a new one containing a new key.
        var previousAuth = await db.Auth
            .Include(a => a.User)
            .Include(a => a.AccountRep)
            .FirstOrDefaultAsync(u => u.UserUID == uid)
            .ConfigureAwait(false);

        // Remove the outdated auth if it exists.
        if (previousAuth is not null)
            db.Auth.Remove(previousAuth);

        var computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        var auth = new Auth()
        {
            HashedKey = StringUtils.Sha256String(computedHash),
            User = previousAuth.User,
            PrimaryUserUID = previousAuth.PrimaryUserUID,
            AccountRep = previousAuth.AccountRep
        };

        embed.WithTitle($"Recovery for {uid} complete");
        embed.WithDescription("This is your new private secret key. Do not share this private secret key with anyone. **If you lose it, it is irrevocably lost.**"
                              + Environment.NewLine + Environment.NewLine
                              + $"**{computedHash}**"
                              + Environment.NewLine + Environment.NewLine
                              + "Enter this key in the Sundouleia Service Settings and reconnect to the service.");

        await db.Auth.AddAsync(auth).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
