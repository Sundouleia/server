using SundouleiaAPI.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

// Permissions that set set as universal. Global rather.
// not really in use at the moment, could use as more things arise.
// primarily intended for settings that would normally be in the plugin config, but other users should see.
public class GlobalPermissions
{
    [Key] // User that these global permissions belong to.
    public string UserUID { get; set; }

    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    // Default Sync-Related preferences when initializing a pair with someone.
    public bool DefaultAllowAnimations  { get; set; } = true;
    public bool DefaultAllowSounds      { get; set; } = true;
    public bool DefaultAllowVfx         { get; set; } = true;

    // Default Moodles related permissions when initializing a pair with someone.
    public MoodleAccess DefaultMoodleAccess     { get; set; } = MoodleAccess.None;
    public TimeSpan     DefaultMaxMoodleTime    { get; set; } = TimeSpan.Zero;
    public bool         DefaultShareOwnMoodles  { get; set; } = false;

    // Other stuff that can be added later on..
}