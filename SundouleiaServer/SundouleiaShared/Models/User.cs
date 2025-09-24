using System.ComponentModel.DataAnnotations;
using SundouleiaAPI.Enums;

namespace SundouleiaShared.Models;

/// <summary> Represents a user profile in the system. </summary>
public class User
{
    [Key]
    [MaxLength(10)]
    public string       UID         { get; set; } // Identifier

    [MaxLength(15)]
    public string       Alias       { get; set; } // Vanity-Created UID, (for Patreon)
    public DateTime     CreatedAt   { get; set; } // When the User entry was created. (UTC format).
    public DateTime     LastLogin   { get; set; } // When last connection was made.
    public CkVanityTier Tier        { get; set; } = CkVanityTier.NoRole;
}
