using System.ComponentModel.DataAnnotations;

namespace SundouleiaShared.Models;

/// <summary>
/// The UserRequest Model stores the Userdata of the user who send the request
/// and the use who received the request.
/// <para>
/// All sent requests expire after 3 days, (and are automatically rejected).
/// </para>
/// </summary>
public class PairRequest
{
    // User that sent the request to add OtherUser.
    [Key]
    [MaxLength(10)]
    public string UserUID { get; set; }
    public User User { get; set; }

    // The target user of this request.
    [Key]
    [MaxLength(10)]
    public string OtherUserUID { get; set; }
    public User OtherUser { get; set; }

    // The time the request was created. **Expires in 3 days after creation.
    [Required]
    public DateTime CreationTime { get; set; } = DateTime.MinValue;

    // If the request is for a temporary pairing, or a permanent one.
    public bool IsTemporary { get; set; } = false;

    // Optionally attached message.
    public string AttachedMessage { get; set; } = string.Empty;
}
