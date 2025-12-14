using SundouleiaAPI.Enums;
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
    public User   User          { get; set; }

    [MaxLength(10)]
    public string OtherUserUID  { get; set; }
    public User   OtherUser     { get; set; }

    // Sync related permissions.
    public bool PauseVisuals    { get; set; } = false; // If all syncing should be paused with this person.
    public bool AllowAnimations { get; set; } = true;  // if modded animation should sync.
    public bool AllowSounds     { get; set; } = true;  // if modded sfx should should sync.
    public bool AllowVfx        { get; set; } = true;  // if modded vfx should should sync.

    // Default Moodles related permissions when initializing a pair with someone.
    public MoodleAccess MoodleAccess    { get; set; } = MoodleAccess.None;
    public TimeSpan     MaxMoodleTime   { get; set; } = TimeSpan.Zero;

    // If we should share limited/full moodles information (avoid full for now)
    public bool         ShareOwnMoodles { get; set; } = false;
}