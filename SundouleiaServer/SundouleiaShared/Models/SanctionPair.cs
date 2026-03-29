using SundouleiaAPI.Data.Permissions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

public class SanctionPair
{
    [Key]
    [Column(Order = 0)]
    public string SanctionID { get; set; }
    [ForeignKey(nameof(SanctionID))]
    public SanctionedGroup Sanction { get; set; }
    
    [Key]
    [Column(Order = 1)]
    public string SanctionUserUID { get; set; }
    [ForeignKey(nameof(SanctionUserUID))]
    public virtual User SanctionUser { get; set; }

    public int[] RoleIds { get; set; } // The roles assigned to this user (A bit unsure on this and may revise later)
    public SanctionAccess Access { get; set; } = SanctionAccess.None; // The determined access this user has within the group
    public DateTime JoinedAtUTC { get; set; } = DateTime.UtcNow;
}