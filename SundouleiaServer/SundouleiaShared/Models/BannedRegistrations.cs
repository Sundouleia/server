using System.ComponentModel.DataAnnotations;

namespace SundouleiaShared.Models;

/// <summary>
///     I really didn't want to add this class, but i guess there are going to 
///     be some bad apples that will try to ruin this experience for everyone, 
///     wont there?
/// </summary>
public class BannedRegistrations
{
    // ID of the discord user who was banned, preventing them from using the discord service.
    [Key]
    [MaxLength(100)]
    public string DiscordId { get; set; }
}
