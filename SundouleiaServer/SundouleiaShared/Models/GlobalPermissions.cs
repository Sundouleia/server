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

    // Various global settings a user can set.
    // can probably figure this out as time goes on, but useful to have for the future.

    // What is set initially upon adding someone.
    public bool DefaultAllowAnimations  { get; set; } = false;
    public bool DefaultAllowSounds      { get; set; } = false;
    public bool DefaultAllowVfx         { get; set; } = false;

    // Other stuff that can be added later on..
}