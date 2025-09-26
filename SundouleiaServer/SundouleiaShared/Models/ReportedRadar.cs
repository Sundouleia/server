using SundouleiaAPI.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///     In the case that a radar is being exploited for purposes it is not made for,
///     they can be reported here.
/// </summary>
public class ReportedRadar
{
    [Key] // Ensure report uniqueness.
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ReportID { get; set; }

    // Require knowing what kind of radar report this is, the time it was made, and what territory it is in.
    [Required] public ReportKind Kind { get; set; } = ReportKind.Radar;
    [Required] public DateTime ReportTime { get; set; }
    [Required] public ushort WorldId { get; set; }
    [Required] public ushort TerritoryId { get; set; }

    // If the reported type was chat, a compressed string of the latest chat history
    // can be collected here, along with the reported UserUID.
    public string RecentRadarChatHistory { get; set; } = string.Empty;
    public string ReportedUserUID { get; set; } = string.Empty;

    // Additional information about where if the territory was indoors. Not set if outdoors.
    public bool IsIndoor { get; set; } = false;
    public byte ApartmentDivision { get; set; }
    public byte PlotIndex { get; set; }
    public byte WardIndex { get; set; }
    public byte RoomNumber { get; set; }

    // Reporter.
    [ForeignKey(nameof(Reporter))]
    public string ReporterUID { get; set; }
    public User Reporter { get; set; }

    // Reason for report.
    public string ReportReason { get; set; } = string.Empty;
}