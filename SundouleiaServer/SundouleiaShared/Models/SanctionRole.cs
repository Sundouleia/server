using SundouleiaAPI.Data.Permissions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   Associated with a group. Has style and permissions.
/// </summary>
public class SanctionRole
{
    [Key] // Unique key generated on creation.
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string RoleId { get; set; }

    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public SanctionedGroup Sanction { get; set; }

    [Required]
    public string Name { get; set; }
    public int Priority { get; set; } = 0; // higher = more authority
    public uint Color { get; set; } = uint.MaxValue;
    public uint AccentColor { get; set; } = uint.MinValue;

    // Not authorative, rather what is added or removed with the role.
    // The GroupPairs permissions itself can be manipulated by others with such access
    public SanctionAccess AssignedAccess { get; set; } = SanctionAccess.None;
}