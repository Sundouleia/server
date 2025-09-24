using System.ComponentModel.DataAnnotations;

namespace SundouleiaShared.Models;

/// <summary>
///     I really didn't want to add this class, but i guess there are going to 
///     be some bad apples that will try to ruin this experience for everyone, 
///     wont there?
/// </summary>
public class Banned
{
    [Key]
    [MaxLength(100)]
    public string CharacterIdentification { get; set; } // indent (identifier) of the character that was banned.
    public string UserUID { get; set; }
    public string Reason { get; set; }
}
