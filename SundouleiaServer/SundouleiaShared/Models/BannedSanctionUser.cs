using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

public class BannedSanctionUser
{
    [Key]
    [Column(Order = 0)]
    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual SanctionedGroup Sanction { get; set; }

    [Key]
    [Column(Order = 1)]
    public string BannedUserUID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public virtual User BannedUser { get; set; }

    [Required]
    public string BannedByUserUID { get; set; }
    [ForeignKey(nameof(BannedByUserUID))] 
    public virtual User BannedByUser  { get; set; }

    public DateTime BannedAtUTC { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = string.Empty;
}
