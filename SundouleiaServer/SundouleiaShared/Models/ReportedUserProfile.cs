using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///     Stores information for moderation review on reports regarding a users profile.
/// </summary>
public class ReportedUserProfile
{
    [Key] // Ensure report uniqueness.
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ReportID { get; set; }

    // Capture details of the profile at the time of the report.
    [Required]
    public DateTime ReportTime { get; set; }
    [Required]
    public string Base64AvatarSnapshot { get; set; } // snapshot the pic at time of report so they cant remove it later.
    [Required]
    public string DescriptionSnapshot { get; set; } // snapshot the description at time of report so they cant remove it later.

    // Player who made the report.
    [ForeignKey(nameof(ReportingUser))]
    public string ReportingUserUID { get; set; }
    public User ReportingUser { get; set; }

    // User being reported.
    [ForeignKey(nameof(ReportedUser))]
    public string ReportedUserUID { get; set; }
    public User ReportedUser { get; set; }

    // store the reason for the report.
    public string ReportReason { get; set; }
}