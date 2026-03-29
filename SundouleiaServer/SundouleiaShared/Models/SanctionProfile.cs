using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

public class SanctionProfile
{
    [Key]
    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual SanctionedGroup Sanction { get; set; }

    public bool IsNSFW { get; set; } = false;
    public bool Reported { get; set; } = false;

    // Sanction Profiles have a banner and logo
    public string Base64Banner { get; set; } = string.Empty;
    public string Base64Logo { get; set; } = string.Empty;
    public uint BgColor { get; set; } = 0xFF000000;
    public uint PrimaryColor { get; set; } = 0xFF888888;
    public uint SecondaryColor { get; set; } = 0xFF444444;
    public uint AccentColor { get; set; } = 0xFF770000;

    // Details
    public string Punchline { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public byte OpenDaysBitMask { get; set; } = 0;
    // Open to additions later and stuff i guess.
    public int[] GroupTags { get; set; } // Maybe revise? Idk how optimal this is.

    public bool UseVenueScope { get; set; } = false; // Future VenueScope integration maybe.
}