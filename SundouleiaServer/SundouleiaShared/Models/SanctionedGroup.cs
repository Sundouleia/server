using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   SanctionedGroups are similar to 'syncshells' but behave quite differently.
///   These can scale infinitely, and are binded to mutable housing locations.
/// </summary>
public class SanctionedGroup
{
    [Key] // Unique Identifier used on creation.
    [MaxLength(20)]
    public string SanctionID { get; set; }

    public string OwnerUID { get; set; }
    public User OwnerUser { get; set; }

    [Required]
    [MaxLength(40)]
    public string Name { get; set; }

    // The estate this group is bound to. We should have a way to identify if this is stale or not.
    public ulong EstateHouseID { get; set; } = 0;

    // Style
    public int IconId { get; set; } = 0;
    public uint IconColor { get; set; } = 0xFFFFFFFF;
    public uint LabelColor { get; set; } = 0xFFFFFFFF;
    public uint BorderColor { get; set; } = 0xFFFFFFFF;
    public uint GradientColor { get; set; } = 0xFF222222;

    public bool IsPublic { get; set; } = true;
    public string Password { get; set; } = string.Empty;
    public string ChatlogId { get; set; } = string.Empty; // Make indexable, also this is WIP
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [ForeignKey(nameof(SanctionID))]
    public virtual SanctionProfile Profile { get; set; }
    
    // Preferences
    public bool AllowAnimationsPreferred { get; set; } = true; 
    public bool AllowSoundsPreferred { get; set; } = true;
    public bool AllowVfxPreferred { get; set; } = true;

    // Roles that belong to this group
    public virtual ICollection<SanctionRole> Roles { get; set; } = new List<SanctionRole>();
}