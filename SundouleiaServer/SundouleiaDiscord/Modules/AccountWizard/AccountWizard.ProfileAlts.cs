using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using SundouleiaShared.Data;
using SundouleiaShared.Models;
using SundouleiaShared.Utils;
using System.Globalization;

namespace SundouleiaDiscord.Modules.AccountWizard;

public partial class AccountWizard
{
    [ComponentInteraction("wizard-alt-profile")]
    public async Task ComponentAltProfile()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentAltProfile), Context.Interaction.User.Id);

        using var sundouleiaDb = await GetDbContext().ConfigureAwait(false);
        // fetch the primary account UID associated with the UID we are wanting to create.
        var primaryUID = (await sundouleiaDb.AccountClaimAuth.Include(u => u.User).SingleAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false)).User.UID;
        var secondaryUids = await sundouleiaDb.Auth.CountAsync(p => p.PrimaryUserUID == primaryUID).ConfigureAwait(false);
        int remainingProfilesAllowed = 10 - secondaryUids;
        EmbedBuilder eb = new();
        eb.WithColor(Color.Gold);
        eb.WithTitle("Add Account Profile");
        eb.WithDescription("Profiles have separate group folders, pairs, and permissions.\n\n"
            + $"You may create {remainingProfilesAllowed} more Profiles. (Capped at 10)");
        ComponentBuilder cb = new();
        AddHome(cb);
        cb.WithButton("Create Secondary UID", "wizard-alt-profile-create:" + primaryUID, ButtonStyle.Primary, emote: new Emoji("2️⃣"), disabled: secondaryUids >= 10);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-alt-profile-create:*")]
    public async Task ComponentNewAltCharProfileCreate(string primaryUid)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{primary}", nameof(ComponentNewAltCharProfileCreate), Context.Interaction.User.Id, primaryUid);

        // fetch the db context.
        using var sundouleiaDb = await GetDbContext().ConfigureAwait(false);
        EmbedBuilder eb = new();
        // log the title for the creation of a new alt character profile
        eb.WithTitle("Alt Character Profile Created!");
        eb.WithColor(Color.Gold);
        ComponentBuilder cb = new();
        AddHome(cb);
        // handle the creation of a new alt character profile
        await HandleAddAltProfile(sundouleiaDb, eb, primaryUid).ConfigureAwait(false);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    public async Task HandleAddAltProfile(SundouleiaDbContext db, EmbedBuilder embed, string primaryUID)
    {
        // Locate the account's main profile user.
        var accountMain = await db.AccountReputation.Include(r => r.User).AsNoTracking().SingleAsync(r => r.UserUID == primaryUID).ConfigureAwait(false);

        // while the UID is not unique, generate a new one.
        var hasValidUid = false;
        var generatedUid = string.Empty;
        while (!hasValidUid)
        {
            var uid = StringUtils.GenerateRandomString(10);
            if (await db.Users.AsNoTracking().AnyAsync(u => u.UID == uid || u.Alias == uid).ConfigureAwait(false))
                continue;
            generatedUid = uid;
            hasValidUid = true;
        }

        // Create the new User and Auth entries for the alt profile.
        User newUser = new()
        {
            UID = generatedUid,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow,
            Tier = accountMain.User.Tier
        };
        // compute the secret key for the user, and initialize the auth as an alt character profile, linking it to the primary account.
        var computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        var auth = new Auth()
        {
            HashedKey = StringUtils.Sha256String(computedHash),
            UserUID = newUser.UID,
            User = newUser,
            PrimaryUserUID = primaryUID,
            PrimaryUser = accountMain.User,
            AccountRep = accountMain
        };

        // add the rows to the user and the auth tables in the database, and save the changes.
        await db.Users.AddAsync(newUser).ConfigureAwait(false);
        await db.Auth.AddAsync(auth).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);

        // output the window contents.
        embed.WithDescription("Copy the Secret Key Provided below and paste it in the Account Creation Section of your Sundouleia Settings for the character you wish to add it to.");
        embed.AddField("UID", newUser.UID);
        embed.AddField("Secret Key", computedHash);
    }

}
