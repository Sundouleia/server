using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///     Defines a relation where <see cref="UserUID"/> has blocked <see cref="OtherUserUID"/>."
///     <b>User</b> does not need to be paired with <b>OtherUser</b> to block them. <para />
///     
///     Notice: Setup lookups to check both directions. A report is not made for both directions. <para />
///     
///     Helps prevent unwanted interactions between users when distance needs to be set. <para />
///     <b>THIS SHOULD ALWAYS REFLECT THE USER'S PRIMARY ACCOUNT USERUID</b>
/// </summary>
public class BlockedUser
{
    // Note that both User and OtherUser have blocked each other effectively via this single entry.
    [Key]
    [Column(Order = 0)]
    [MaxLength(10)]
    public string UserUID { get; set; }
    public User User { get; set; }

    [Key]
    [Column(Order = 1)]
    [MaxLength(10)]
    public string OtherUserUID { get; set; }
    public User OtherUser { get; set; }
}
