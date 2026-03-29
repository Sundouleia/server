using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///     Defines a request sent from <b>User</b> to <b>OtherUser</b> <para />
///     Sent requests expire after 3 days, (and are automatically rejected).
/// </summary>
public class PairRequest
{
    // User that sent the request to add OtherUser.
    [Key]
    [Column(Order = 0)]
    [MaxLength(10)]
    public string UserUID { get; set; }
    public User User { get; set; }

    // The target user of this request.
    [Key]
    [Column(Order = 1)]
    [MaxLength(10)]
    public string OtherUserUID { get; set; }
    public User OtherUser { get; set; }

    // The time the request was created. **Expires in 3 days after creation.
    [Required]
    public DateTime CreationTime { get; set; } = DateTime.MinValue;

    // If the request is for a temporary pairing, or a permanent one.
    public bool IsTemporary { get; set; } = false;

    // Preferred nickname to assign to OtherUser upon acceptance.
    public string PreferredNickname { get; set; } = string.Empty;

    // Optionally attached message.
    public string AttachedMessage { get; set; } = string.Empty;

    // Sent from WorldId and ZoneId.
    public ushort FromWorldId { get; set; } = 0;
    public ushort FromZoneId { get; set; } = 0;
}

// Update this later to help reflect if the requests that we get are for
// radar pairs or direct pairs, since they are seprate things.
