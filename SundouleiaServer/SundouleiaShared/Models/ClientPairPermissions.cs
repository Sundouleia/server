using System.ComponentModel.DataAnnotations;

namespace SundouleiaShared.Models;

/// <summary>
///     The permissions that <b>UserUID</b> has set for <b>OtherUserUID</b>. <para />
///     This is not bi-directional. Each user defines their permissions for each other.
/// <summary/>
public class ClientPairPermissions
{
    [MaxLength(10)]
    public string UserUID       { get; set; }
    public User User            { get; set; }


    [MaxLength(10)]
    public string OtherUserUID  { get; set; }
    public User OtherUser       { get; set; }

    // Various permissions for individuals. May be updated overtime.
    public bool PauseVisuals    { get; set; } = false; // If all syncing should be paused with this person.
    public bool AllowAnimations { get; set; } = true;  // if modded animation should sync.
    public bool AllowSounds     { get; set; } = true;  // if modded sfx should should sync.
    public bool AllowVfx        { get; set; } = true;  // if modded vfx should should sync.

    // Might add this, but unsure.
    //public MoodlePerms MoodlePerms               { get; set; } = MoodlePerms.None;
    //public TimeSpan    MaxMoodleTime             { get; set; } = TimeSpan.Zero;
}