using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Enums;
using SundouleiaDiscord.Modules.Popups;
using SundouleiaShared.Data;
using System.Security.Cryptography;
using System.Text;
using DiscordConfig = SundouleiaShared.Utils.Configuration.DiscordConfig;

namespace SundouleiaDiscord.Modules.AccountWizard;

/// <summary>
/// This class will be heavily modified to remove lodestone linking entirely, and be replaced with a verification modal.
/// </summary>
public partial class AccountWizard
{
    /// <summary>
    /// The component interaction for what will display when we press the interaction button for claiming an account.
    /// </summary>
    [ComponentInteraction("wizard-claim")]
    public async Task ComponentRegister()
    {
        // as always, validate the interaction. If its valid, log it, if it isnt, return.
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegister), Context.Interaction.User.Id);

        // create a new embed builder to update the current one with the new menu display.
        EmbedBuilder eb = new();
        eb.WithColor(Color.Gold);
        eb.WithTitle("Account Claim Process");
        eb.WithDescription("Sundouleia's Claim Process ensures verification does not interface with lodestone or any SquareEnix interfaces.\n\n"
            + "**Verification is instead handled via Dalamud's overlay UI**\n\n"
            + "### Before you begin, make that:\n\n"
            + " 🔘 You are logged into FFXIV and connected to Sundouleia.\n\n"
            + " 🔘 You can copy your account's secret key. (In Sundouleias `Settings > Accounts` UI)");
        ComponentBuilder cb = new();
        AddHome(cb); // add the home button so we can go back at any point
        cb.WithButton("I'm Ready!", "wizard-register-start", ButtonStyle.Primary, emote: new Emoji("⏭️")); // button to start claim process.
        await ModifyInteraction(eb, cb).ConfigureAwait(false); // modify the message currently displaying the homepage details.
    }

    /// <summary>
    /// Called upon whenever the user hits the "Begin the Account Claim Process" button in the claim account menu.
    /// </summary>
    [ComponentInteraction("wizard-register-start")]
    public async Task ComponentRegisterStart()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegisterStart), Context.Interaction.User.Id);

        // grab the database context
        using var db = await GetDbContext().ConfigureAwait(false);
        // if we enter this menu at all, for whatever reason, we should remove the user from the claimauth table, and the initial key mapping.
        var entry = await db.AccountClaimAuth.SingleOrDefaultAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false);
        if (entry != null)
            db.AccountClaimAuth.Remove(entry);
        _botServices.DiscordInitialKeyMapping.TryRemove(Context.User.Id, out _);
        _botServices.DiscordVerifiedUsers.TryRemove(Context.User.Id, out _);

        // save changes to the DB and fire the initial Key modal.
        await db.SaveChangesAsync().ConfigureAwait(false);

        // display the popup model asking for the initial secret key the plugin generated for the user.
        await RespondWithModalAsync<InitialKeyModal>("wizard-claim-account-modal").ConfigureAwait(false);
    }

    /// <summary>
    /// Called upon by the registration start model, and will prompt the user to provide them with the initial key generated for them.
    /// </summary>
    /// <param name="initialkeymodal"> the modal to display the initial key prompt</param>
    [ModalInteraction("wizard-claim-account-modal")]
    public async Task ModalRegister(InitialKeyModal initialKeyModal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{initial_key}", nameof(ModalRegister), Context.Interaction.User.Id, initialKeyModal.InitialKeyStr);

        // create the embed builder where we make the color purple, and then prompt the user with the registration modal.
        EmbedBuilder eb = new();
        eb.WithColor(Color.Gold);
        // provide the registration modal and await the response, returns if the registration was successful or not, and the verification code.
        bool success = HandleRegisterModalAsync(eb, initialKeyModal);
        // while we handle the registration for the modal, construct the component builder allowing the user to cancel, verify, or try again.
        ComponentBuilder cb = new();
        cb.WithButton("Cancel", "wizard-claim", ButtonStyle.Secondary, emote: new Emoji("❌"));

        // if the modal returned successful, allow them to verify (pass in item2, the verification)
        if (success) cb.WithButton("Send Verification Code to Client", "wizard-claim-verify-start:"+initialKeyModal.InitialKeyStr, ButtonStyle.Primary, emote: new Emoji("✅"));
        // otherwise, ask them to try again, stepping back to where we ask them for the initial key. Often we get here is the key is not correct.
        else cb.WithButton("Try again", "wizard-claim-start", ButtonStyle.Primary, emote: new Emoji("🔁"));
        await ModifyModalInteraction(eb, cb).ConfigureAwait(false);
    }

    /// <summary>
    /// Fired once we hit the verify button in our registration modal screen. Displays a new model for inserting the verification code.
    /// </summary>
    /// <param name="verificationCode"> the code passed in that we will be comparing against the prompt requesting it to validate our account. </param>
    [ComponentInteraction("wizard-claim-verify-start:*")]
    public async Task ComponentRegisterVerify(string initialKeyStr)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{verificationcode}", nameof(ComponentRegisterVerify), Context.Interaction.User.Id, initialKeyStr);

        // contain logic for sending the updated information to the client here
        using var scope = _services.CreateScope();
        using var db = scope.ServiceProvider.GetService<SundouleiaDbContext>();

        // now that we had ensured we have a valid initial key, we can generate a accountclaimauth row in the database for our user
        string verificationCode = await GenerateAccountClaimAuth(Context.User.Id, initialKeyStr, db).ConfigureAwait(false);
        // store the verification code
        var tuple = _botServices.DiscordInitialKeyMapping[Context.User.Id];
        tuple.Item2 = verificationCode;
        // store it back in the mapping
        _botServices.DiscordInitialKeyMapping[Context.User.Id] = tuple;

        // display the popup model asking for the initial secret key the plugin generated for the user.
        await RespondWithModalAsync<VerificationModal>("wizard-claim-verify-check").ConfigureAwait(false);
    }

    /// <summary>
    /// Called upon after submitting a verification code, returning if the outcome is sucessful or not.
    /// </summary>
    /// <param name="verificationCode"></param>
    /// <returns></returns>
    [ModalInteraction("wizard-claim-verify-check")]
    public async Task ComponentRegisterVerifyCheck(VerificationModal verificationCodeModal)
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}:{uid}", nameof(ComponentRegisterVerifyCheck), Context.Interaction.User.Id, verificationCodeModal);

        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        eb.WithColor(Color.Gold);
        // await the finish of the model
        (bool success, string uid, string key) = await HandleVerificationModalAsync(eb, verificationCodeModal).ConfigureAwait(false);

        if (success)
        {
            eb.WithColor(Color.Green);
            eb.WithTitle($"[{uid}] Is now your Primary Account Profile.");
            eb.WithDescription($"**Save this key! If you lose it, all other profiles of this account will be lost.**\n\n"
                + $"**Your UID is:** ```{uid}```\n"
                + $"**Your Secret Key is:** ```{key}```");
            AddHome(cb);
        }
        else
        {
            eb.WithColor(Color.Gold);
            eb.WithTitle("Failed to Claim Account");
            eb.WithDescription("Unable to claim your account. One of the following occurred:\n"
                + "- The verification time expired\n"
                + "- The code was incorrect.\n\n"
                + "Please restart your verification process.");
            cb.WithButton("Cancel", "wizard-claim", ButtonStyle.Secondary, emote: new Emoji("❌"));
        }
        await ModifyModalInteraction(eb, cb).ConfigureAwait(false);

    }

    /// <summary>
    /// Called by the start of the registration when asking for the initial key.
    /// </summary>
    /// <param name="embed"> the embed builder for the message </param>
    /// <param name="arg"> the initial key modal as an argument passed in. </param>
    /// <returns> if it was sucessful or not, and the verification code string. </returns>
    private bool HandleRegisterModalAsync(EmbedBuilder embed, InitialKeyModal arg)
    {
        // at this point in time, remember that we have no accountClaimAuth object, only a user and auth object.
        using var scope = _services.CreateScope();
        using var db = scope.ServiceProvider.GetService<SundouleiaDbContext>();
        // if it is empty, fail the handle.
        if (arg.InitialKeyStr is null)
        {
            embed.WithTitle("Initial key was not provided in the modal. Try again.");
            return false;
        }

        // Get the hashed key.
        using var sha256 = SHA256.Create();
        var hashedKey = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(arg.InitialKeyStr))).Replace("-", "", StringComparison.Ordinal);

        // otherwise, if the initial key is not in our database, then someone is trying to forge it
        if (!db.Auth.Any(a => a.HashedKey == hashedKey))
        {
            embed.WithTitle("This secret key is not being used by any users, or you pasted it in wrong.");
            return false;
        }
        if (db.AccountClaimAuth.Any(a => a.InitialGeneratedKey == hashedKey))
        {
            embed.WithTitle("This secret key has already been claimed by another user.");
            return false;
        }

        embed.WithTitle("Claim Process - Verification");
        embed.WithDescription("A verification code was generated and is ready sent to you.\n\n"
            + "__This will be sent to your currently logged in client__\n\n"
            + $"**Send the code to the client once you are logged in and connected.**\n\n"
            + "Verification will expire in 10minutes starting now.\n"
            + "If you fail to verify, you'll have to register again.");

        // store the initial key to the initial key mapping.
        _botServices.DiscordInitialKeyMapping[Context.User.Id] = (hashedKey, string.Empty);

        // return success with the verification code.
        return true;
    }

    // Handles the result of account verification.
    private async Task<(bool, string, string)> HandleVerificationModalAsync(EmbedBuilder eb, VerificationModal verificationModal)
    {
        // fetch the verification code
        _botServices.DiscordInitialKeyMapping.TryGetValue(Context.User.Id, out var keyValue);
        var initialKey = keyValue.Item1;
        var verificationCode = keyValue.Item2;

        _logger.LogInformation($"Initial key [Initial Key -> ({initialKey}), Verification -> ({verificationCode}), User: {Context.User.Id}]");

        _botServices.DiscordVerifiedUsers.Remove(Context.User.Id, out _);
        // Ensure still valid in mapping context.
        if(!_botServices.DiscordInitialKeyMapping.ContainsKey(Context.User.Id))
        {
            _logger.LogInformation($"User {Context.User.Id} does not have an initial key mapping");
            eb.WithTitle("You have not started the registration process, or you timed out. Please try again.");
            _botServices.DiscordVerifiedUsers[Context.User.Id] = false;
            return (false, string.Empty, string.Empty);
        }

        // Answers must match.
        if (!string.Equals(verificationModal.VerificationCodeStr, verificationCode, StringComparison.Ordinal))
        {
            _logger.LogInformation($"Verification ({verificationModal.VerificationCodeStr}) failed to match generated code ({verificationCode}) for {Context.User.Id}");
            eb.WithTitle("The verification code you entered does not match the one generated for you, try registration again.");
            _botServices.DiscordVerifiedUsers[Context.User.Id] = false;
            return (false, string.Empty, string.Empty);
        }

        using var dbContext = await GetDbContext().ConfigureAwait(false);
        // grab tables to be modified.
        var authClaim = await dbContext.AccountClaimAuth.SingleOrDefaultAsync(u => u.DiscordId == Context.User.Id).ConfigureAwait(false);
        var auth = await dbContext.Auth.Include(a => a.User).SingleOrDefaultAsync(u => u.HashedKey == initialKey).ConfigureAwait(false);
        var rep = await dbContext.AccountReputation.SingleOrDefaultAsync(r => r.UserUID == auth.PrimaryUserUID).ConfigureAwait(false);
        var user = auth.User;

        // If any of the fetched table entries are null, fail it.
        if (authClaim is null || auth is null || user is null || rep is null)
        {
            _logger.LogInformation($"Keys matched, but failed to fetch AccountClaimAuth, Auth, or User for {Context.User.Id} with initial key {initialKey}");
            eb.WithTitle("Keys matched, but failed to fetch your account information. Please try again.");
            _botServices.DiscordVerifiedUsers[Context.User.Id] = false;
            return (false, string.Empty, string.Empty);
        }

        // Everything is valid, so change values in the table entries.
        _logger.LogInformation($"Verification ({verificationModal.VerificationCodeStr}) matched generated code for: ({Context.User.Id})");
        _botServices.DiscordVerifiedUsers[Context.User.Id] = true;

        // Clear code, key, (and started at, as cleanup service removes all non-null startedAt items).
        authClaim.InitialGeneratedKey = null;
        authClaim.StartedAt = null;
        authClaim.VerificationCode = null;

        // declare the user since it is now verified.
        authClaim.User = user;

        // mark the user as verified and set the last login time to now.
        user.LastLogin = DateTime.UtcNow;

        // set Verified to true for the rep.
        rep.IsVerified = true;

        dbContext.Update(authClaim);
        dbContext.Update(user);
        dbContext.Update(rep);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        // Do a personalized vanity role update check.
        _logger.LogInformation("Grabbing supporter roles from both discords for personalized update..");
        var sundouleiaRoles = _discordConfig.GetValueOrDefault(nameof(DiscordConfig.VanityRoles), new Dictionary<ulong, string>());
        var ckRoles = _discordConfig.GetValueOrDefault(nameof(DiscordConfig.CkVanityRoles), new Dictionary<ulong, string>());
        var sundouleiaRoleToId = sundouleiaRoles.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);
        // Grab the context discord user from sundouleia.
        var sundouleiaUser = await _botServices.SundouleiaGuildCached.GetUserAsync(Context.User.Id).ConfigureAwait(false);
        // If they do not contain any roles, check if they have any roles in ck first, and if they do, assign them.
        if (!sundouleiaUser.RoleIds.Any(sundouleiaRoles.ContainsKey))
        {
            // Scope into ck and check their user there.
            if (await _botServices.CkGuildCached.GetUserAsync(authClaim.DiscordId).ConfigureAwait(false) is { } ckUser)
            {
                // get their roleIds.
                var roleIds = ckUser.RoleIds;
                _logger.LogInformation($"User {authClaim.User!.UID} has CK Roles: {string.Join(", ", roleIds)}");
                // Determine the highest role to check against by it's priority.
                var highestRoleId = roleIds.Where(ckRoles.ContainsKey).OrderByDescending(id => DiscordBot.RoleToVanityTier[ckRoles[id]]).FirstOrDefault();
                // Get the string name of this role.
                if (ckRoles.TryGetValue(highestRoleId, out var priorityRole) && DiscordBot.CkToSundRoles.TryGetValue(priorityRole, out var matchingSundRoles))
                {
                    // Assign all matching sundouleiaRoles to the user.
                    var rolesToAssign = matchingSundRoles.Select(r => sundouleiaRoleToId[r]).ToList();
                    await sundouleiaUser.AddRolesAsync(rolesToAssign).ConfigureAwait(false);
                    _logger.LogInformation($"Assigned Roles from Ck Supporter {authClaim.User!.UID} assigned roles: {string.Join(", ", matchingSundRoles)}");
                }
            }
        }

        // Now we should check their roles in sundouleia, and assign the highest role they have.
        if (sundouleiaUser.RoleIds.Any(ckRoles.ContainsKey))
        {
            // fetch the roles they have, and output them.
            _logger.LogInformation($"User {authClaim!.User!.UID} has roles: {string.Join(", ", sundouleiaUser.RoleIds)}");
            // Determine the highest priority role
            CkVanityTier highestRole = sundouleiaUser.RoleIds
                .Where(sundouleiaRoles.ContainsKey)
                .Select(id => DiscordBot.RoleToVanityTier[sundouleiaRoles[id]])
                .OrderByDescending(tier => tier)
                .FirstOrDefault();

            // Assign Highest role found.
            authClaim.User.Tier = highestRole;
            dbContext.Update(authClaim.User);
            _logger.LogInformation($"User {authClaim.User.UID} assigned to tier {highestRole} (highest role)");

            // Update this on all secondary accounts of this user.
            var altProfiles = await dbContext.Auth.Include(a => a.User).AsNoTracking().Where(a => a.PrimaryUserUID == authClaim.User.UID).ToListAsync().ConfigureAwait(false);
            foreach (var profile in altProfiles)
            {
                _logger.LogDebug($"AltProfile [{profile.User.UID}] also given this perk!");
                profile.User.Tier = highestRole;
                dbContext.Update(profile.User);
            }
        }
        // await for the database to save changes
        await dbContext.SaveChangesAsync().ConfigureAwait(false);


        // return success with the user's UID
        return (true, user.UID, initialKey);
    }
}
