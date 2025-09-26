using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;


// Reputation resolving around said user.
// Determines if a user is verified, or what restrictions are placed upon them.
// Also provides the time they were applied, if they have timeout periods.and when they got applied.
// Useful as a form of moderation, to keep the 'wild people' in check. *catscream*
/// <summary>
///     Reputation resolving around said user, determining if they are verified, banned,
///     or have certain restrictions placed upon them. <para />
///     
///     Additionally provides optional timeouts once a report is resolved. When they pass the current
///     time access to the timed out features will be returned. <para />
///     
///     Useful as a form of moderation, to keep the 'wild people' in check. <b>catscream</b>
/// </summary>
public class AccountReputation
{
    [Key]
    public string UserUID { get; set; }
    
    [ForeignKey(nameof(UserUID))] 
    public virtual User User { get; set; }

    public bool IsVerified  { get; set; } = false;  // If the account is connected to the Sundouleia discord bot.
    public bool IsBanned    { get; set; } = false;  // If the account, and all it's profiles, are banned from Sundouleia.

    // Helpers that are unmapped for Ban detection.
    [NotMapped] public int WarningStrikes => ProfileViewStrikes + ProfileEditStrikes + RadarStrikes + ChatStrikes;
    [NotMapped] public bool ShouldBan => WarningStrikes >= 5;
    [NotMapped] public bool NeedsTimeoutReset => ProfileViewTimeout != DateTime.MinValue || ProfileEditTimeout != DateTime.MinValue || RadarTimeout != DateTime.MinValue || ChatTimeout != DateTime.MinValue;

    // Reputations are outlined as follows:
    // - If they can do it.
    // - If timed out, when it expires. (in UTC)
    // - How many times they were timed out for this.

    // Reputation for viewing other user profiles.
    public bool     ProfileViewing      { get; set; } = true;
    public DateTime ProfileViewTimeout  { get; set; } = DateTime.MinValue;
    public int      ProfileViewStrikes  { get; set; } = 0;

    // Reputation for customizing profiles.
    public bool     ProfileEditing     { get; set; } = true;
    public DateTime ProfileEditTimeout { get; set; } = DateTime.MinValue;
    public int      ProfileEditStrikes { get; set; } = 0;

    // Reputation for Radar usage.
    public bool     RadarUsage      { get; set; } = true;
    public DateTime RadarTimeout    { get; set; } = DateTime.MinValue;
    public int      RadarStrikes    { get; set; } = 0;

    // Reputation for Radar Chat usage.
    public bool     ChatUsage   { get; set; } = true;
    public DateTime ChatTimeout { get; set; } = DateTime.MinValue;
    public int      ChatStrikes { get; set; } = 0;

    // Additional reputation states can be setup here as time passes.
}