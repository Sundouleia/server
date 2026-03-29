using System.ComponentModel.DataAnnotations;
using SundouleiaAPI.Enums;

namespace SundouleiaShared.Models;

/// <summary> Represents the user's core data in the system. </summary>
public class User
{
    /// <summary> The User Identity </summary>
    [Key]
    [MaxLength(10)]
    public string       UID         { get; set; }

    /// <summary> The Alias display of the user. (Intended for supporters) </summary>
    [MaxLength(15)]
    public string       Alias           { get; set; }

    /// <summary> When the user was created (UTC format) </summary>
    public DateTime     CreatedAt       { get; set; }

    /// <summary> When the user last connected to the service. (UTC format) </summary>
    public DateTime     LastLogin       { get; set; }

    /// <summary> What supporter tier this user currently has, if any </summary>
    public CkVanityTier Tier            { get; set; } = CkVanityTier.NoRole;

    /// <summary> The name prioritized over Anon-User if set (For supporters) </summary>
    /// <remarks> Used for chat and sanctioned groups </remarks>
    [MaxLength(25)]
    public string       DisplayName     { get; set; } = string.Empty;

    /// <summary> The color applied to the name (For supporters) </summary>
    public uint         NameColor       { get; set; } = uint.MaxValue;

    /// <summary> The glow color applied to the name (For supporters) </summary>
    public uint         NameGlowColor   { get; set; } = uint.MinValue;

    // Could add text color fields here but that would make chat spaghetti eyesore.
}

