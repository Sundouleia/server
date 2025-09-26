using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;
#pragma warning disable CS8632

/// <summary>
///     Defines both <seealso cref="UserUID"/> and <seealso cref="PrimaryUserUID"/> on creation. <para />
///     
///     With this knowledge we will always have access to account-wide reputation from an
///     auth lookup. The Attributes allow this to function even if UserUID == PrimaryUserUID.
/// </summary>
public class Auth
{
    // The "Secret Key" for a profile.
    [Key]
    [MaxLength(64)]
    public string HashedKey { get; set; }

    [Required]
    public string UserUID { get; set; }
    
    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    [Required]
    public string PrimaryUserUID { get; set; }
    
    [ForeignKey(nameof(PrimaryUserUID))]
    public virtual User PrimaryUser { get; set; }

    [ForeignKey(nameof(PrimaryUserUID))]
    public virtual AccountReputation AccountRep { get; set; }

    [NotMapped] public bool IsPrimary => string.Equals(UserUID, PrimaryUserUID);

    // Designed for efficient loading. Without any includes, only retrieves HashedKey, UserUID, PrimaryUID.
}
#pragma warning restore CS8632