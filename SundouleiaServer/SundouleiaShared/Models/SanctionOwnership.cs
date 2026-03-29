using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;

/// <summary>
///   Defines which locations a user owns in terms of housings. <para />
///   For FC Estates, currently, this requires you to be the owner of the estate.
/// </summary>
/// <remarks> These can only be validated while within housing territories. </remarks>
public class SanctionOwnership
{
    [Key]
    public string UserUID { get; set; }

    [ForeignKey(nameof(UserUID))]
    public virtual User User { get; set; }

    public ulong ApartmentHouseID { get; set; } = 0;
    public ulong PersonalHouseID { get; set; } = 0;
    public ulong FreeCompanyHouseID { get; set; } = 0;

    // Indexable stuff here. Note that these groups can exist, only when this user owns the location.
    // These also cannot be edited while stale
    public string ApartmentSID { get; set; } = string.Empty;
    [ForeignKey(nameof(ApartmentSID))]
    public virtual SanctionedGroup ApartmentGroup { get; set; }

    public string PersonalSID { get; set; } = string.Empty;
    [ForeignKey(nameof(PersonalSID))]
    public virtual SanctionedGroup PersonalGroup { get; set; }

    public string FreeCompanySID { get; set; } = string.Empty;
    [ForeignKey(nameof(FreeCompanySID))]
    public virtual SanctionedGroup FreeCompanyGroup { get; set; }
}