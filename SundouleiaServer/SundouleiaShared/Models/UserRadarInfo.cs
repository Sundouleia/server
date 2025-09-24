using SundouleiaAPI.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary> 
///     Holds information about a user lingering in a territory for radars. <para />
///     
///     Because this needs to have a link to the player themselves we need to store
///     a safe way to identify them in game. We do this by hashing player identity.
/// </summary>
public class UserRadarInfo
{
    [Key]
    [Column(Order = 0)]
    public string UserUID { get; set; }

    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    [Key]
    [Column(Order = 1)]
    public ushort TerritoryId { get; set; }

    [Key]
    [Column(Order = 2)]
    public ushort WorldId { get; set; }

    // Hashed to ensure secure transit without exposing CID over interactions.
    // If the ID is 0, can assume the user is opted out, or the entry was inproperly disposed of.
    public string HashedCID { get; set; }
}
