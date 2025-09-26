using SundouleiaAPI.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;
public class UserProfileData
{
    [Key]
    public string UserUID { get; set; }
    
    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    // If anyone can see any part of the profile at all.
    public bool IsPublic { get; set; } = false;
    public bool IsNSFW { get; set; } = false;

    // Visibility scope preferences.
    public PublicityScope AvatarVis { get; set; } = PublicityScope.Private;
    public PublicityScope DescriptionVis { get; set; } = PublicityScope.Private;
    public PublicityScope DecorationVis { get; set; } = PublicityScope.Private;

    // Moderation Values.
    public bool FlaggedForReport { get; set; } = false;
    public bool IsDisabled { get; set; } = false;

    // Base Information
    public string Base64AvatarData { get; set; } = string.Empty; // string.empty == no image provided.
    public string Description { get; set; } = string.Empty;

    //public int EarnedAchievements { get; set; } = 0; // Should maybe be moved into achievements with a ref to it, if we add it.
    //public int TitleId { get; set; } = 0; // Chosen Achievement Title. 0 == no title chosen.

    public PlateBG MainBG { get; set; } = PlateBG.Default;
    public PlateBorder MainBorder { get; set; } = PlateBorder.Default;

    public PlateBG AvatarBG { get; set; } = PlateBG.Default;
    public PlateBorder AvatarBorder { get; set; } = PlateBorder.Default;
    public PlateOverlay AvatarOverlay { get; set; } = PlateOverlay.Default;

    public PlateBG DescriptionBG { get; set; } = PlateBG.Default;
    public PlateBorder DescriptionBorder { get; set; } = PlateBorder.Default;
    public PlateOverlay DescriptionOverlay { get; set; } = PlateOverlay.Default;

}