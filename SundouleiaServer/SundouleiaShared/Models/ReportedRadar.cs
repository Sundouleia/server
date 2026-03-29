using SundouleiaAPI.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   Stores reports for radar group and radar chat misconduct.
/// </summary>
public class ReportedRadar
{
    [Key] // Ensure report uniqueness.
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ReportID { get; set; }

    // Require knowing what kind of radar report this is, the time it was made, and what territory it is in.
    [Required] public ReportKind Kind { get; set; } = ReportKind.Radar;
    [Required] public DateTime ReportTime { get; set; }

    // Only need to know worldId and TerritoryId for radar reports. Doesnt madder for chat since we have chatlogId
    [Required] public ushort WorldId { get; set; }
    [Required] public ushort TerritoryId { get; set; }

    // Unique to chat reports
    public string ChatLogId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ChatContextJson { get; set; } = string.Empty;

    public string ReportedUserUID { get; set; } = string.Empty;

    // Reporter.
    [ForeignKey(nameof(Reporter))]
    public string ReporterUID { get; set; }
    public User Reporter { get; set; }

    // Reason for report.
    public string ReportReason { get; set; } = string.Empty;
}