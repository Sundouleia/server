using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   Ensures people stop bombing eachother and airstrikes and all that.
/// </summary>
public class BlacklistedUser
{
    [Key]
    [Column(Order = 0)]
    [ForeignKey(nameof(User))]
    public string UserUID { get; set; }
    public User User { get; set; }

    [Key]
    [Column(Order = 1)]
    [ForeignKey(nameof(User))]
    public string BlockedUserUID { get; set; }
    public User BlockedUser { get; set; }

    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;
}
