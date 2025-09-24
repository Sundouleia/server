using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///     Represents <b>USER A</b>'s connection with <b>USER B</b>. This is <b>NOT</b> bidirectional. <para />
///     UserUID and OtherUserUID are both indexed as keys to ensure uniqueness, and be indexable for faster lookups.
/// </summary>
public class ClientPair
{
    [Key] // USER A
    [Column(Order = 0)]
    [MaxLength(10)]
    public string UserUID { get; set; }
    public User User { get; set; }

    [Key] // USER B
    [Column(Order = 1)]
    [MaxLength(10)] // Composite key with UserUID
    public string OtherUserUID { get; set; }
    public User OtherUser { get; set; }

    // Time made. (maybe convert into a datetime or something. so we know when to bomb them later)
    [Timestamp]
    public byte[] Timestamp { get; set; }

    public bool IsTemporary { get; set; } = false;
}
