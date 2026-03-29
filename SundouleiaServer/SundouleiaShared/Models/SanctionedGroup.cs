using SundouleiaAPI.Data.Permissions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   Defines which locations a user owns in terms of housings. <para />
///   For FC Estates, currently, this requires you to be the owner of the estate.
/// </summary>
/// <remarks> These can only be validated while within housing territories. </remarks>
public class SanctionOwnership
{
    [Key]
    public string UserUID { get; set; }

    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    public ulong ApartmentHouseID { get; set; } = 0;
    public ulong PersonalHouseID { get; set; } = 0;
    public ulong FreeCompanyHouseID { get; set; } = 0;

    // Indexable stuff here. Note that these groups can exist, only when this user owns the location.
    // These also cannot be edited while stale
    public string ApartmentSID { get; set; } = string.Empty;
    [ForeignKey(nameof(ApartmentSID))]
    public virtual SanctionedGroup ApartmentGroup { get; set; }

    public string PersonalSID { get; set; } = string.Empty;
    [ForeignKey(nameof(PersonalSID))]
    public virtual SanctionedGroup PersonalGroup { get; set; }

    public string FreeCompanySID { get; set; } = string.Empty;
    [ForeignKey(nameof(FreeCompanySID))]
    public virtual SanctionedGroup FreeCompanyGroup { get; set; }
}


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

    // Preferences
    public bool AllowAnimationsPreferred { get; set; } = true; 
    public bool AllowSoundsPreferred { get; set; } = true;
    public bool AllowVfxPreferred { get; set; } = true;

    // Roles that belong to this group
    public virtual ICollection<SanctionRole> Roles { get; set; } = new List<SanctionRole>();
}

public class SanctionPair
{
    [Key]
    [Column(Order = 0)]
    public string SanctionID { get; set; }
    public SanctionedGroup Sanction { get; set; }
    
    [Key]
    [Column(Order = 1)]
    public string UserUID { get; set; }
    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    public int[] RoleIds { get; set; } // The roles assigned to this user (A bit unsure on this and may revise later)
    public SanctionAccess Access { get; set; } = SanctionAccess.None; // The determined access this user has within the group
    public DateTime JoinedAtUTC { get; set; } = DateTime.UtcNow;
}

public class SanctionRole
{
    [Key] // Uniquely generated on creation. Associated with a group. Has set style and permissions.
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string RoleId { get; set; }

    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public SanctionedGroup Sanction { get; set; }

    [Required]
    public string Name { get; set; }
    public int Priority { get; set; } = 0; // higher = more authority

    // Not authorative, rather what is added or removed with the role.
    // The GroupPairs permissions itself can be manipulated by others with such access
    public SanctionAccess AssignedAccess { get; set; } = SanctionAccess.None;
    public uint Color { get; set; } = uint.MaxValue;
    public uint AccentColor { get; set; } = uint.MinValue;
}

public class SanctionProfile
{
    [Key]
    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual SanctionedGroup Sanction { get; set; }

    public bool IsNSFW { get; set; } = false;
    public bool Reported { get; set; } = false;

    // Sanction Profiles have a banner and logo
    public string Base64Banner { get; set; } = string.Empty;
    public string Base64Logo { get; set; } = string.Empty;
    public uint BgColor { get; set; } = 0xFF000000;
    public uint PrimaryColor { get; set; } = 0xFF888888;
    public uint SecondaryColor { get; set; } = 0xFF444444;
    public uint AccentColor { get; set; } = 0xFF770000;

    // Details
    public string Punchline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public byte OpenDaysBitMask { get; set; } = 0;
    // Open to additions later and stuff i guess.
    public int[] GroupTags { get; set; } // Maybe revise? Idk how optimal this is.

    public bool UseVenueScope { get; set; } = false; // Future VenueScope integration maybe.
}

public class SanctionBan
{
    [Key]
    [Column(Order = 0)]
    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual SanctionedGroup Sanction { get; set; }

    [Key]
    [Column(Order = 1)]
    public string BannedUserUID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual User BannedUser { get; set; }

    public string BannedByUserUID { get; set; }
    [ForeignKey(nameof(BannedByUserUID))] 
    public virtual User BannedByUser  { get; set; }

    public DateTime BannedAtUTC { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
}
