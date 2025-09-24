using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;
#pragma warning disable CS8632

/// <summary>
///     The <b>Auth</b> class represents a user's authentication information. <para />
///     The <b>HashedKey</b> property is a unique identifier, and all auth models 
///     store the primary user UID so we know who the account owner is if it is a secondary account.
/// </summary>
public class Auth
{
    // The "Secret Key" for a profile. The secret key where the UserUID == PrimaryUserUID is the account secret key.
    [Key]
    [MaxLength(64)]
    public string HashedKey { get; set; }        // The "Secret Key" for a profile. The secret key where the UserUID == PrimaryUserUID is the account secret key.

    public string UserUID { get; set; } // Indexed
    public User User { get; set; }

    // Information about the Auth's primary user account, if this auth entry is for an alternate profile.
    public string? PrimaryUserUID { get; set; } // Indexed
    public User? PrimaryUser { get; set; }

    // Fetch the Uid in a single check.
    [NotMapped] public string AccountUserUID => PrimaryUserUID ?? UserUID;
    // Determine if the account is primary or not.
    [NotMapped] public bool IsPrimary => string.IsNullOrEmpty(PrimaryUserUID);
}
#pragma warning restore CS8632