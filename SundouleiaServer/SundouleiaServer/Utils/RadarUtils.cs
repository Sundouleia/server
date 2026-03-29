using SundouleiaAPI.Data;
using System.Collections.ObjectModel;

namespace SundouleiaServer.Utils;
#nullable enable
public static class RadarUtils
{
    public static readonly IReadOnlyList<PlaceRegionData> PlaceData =
    [
        new(22,   "La Noscea",            134,  "Middle La Noscea"),
        new(22,   "La Noscea",            135,  "Lower La Noscea"),
        new(22,   "La Noscea",            137,  "Eastern La Noscea"),
        new(22,   "La Noscea",            138,  "Western La Noscea"),
        new(22,   "La Noscea",            139,  "Upper La Noscea"),
        new(22,   "La Noscea",            180,  "Outer La Noscea"),
        new(22,   "La Noscea",            250,  "Wolves' Den Pier"),
        new(23,   "The Black Shroud",     148,  "Central Shroud"),
        new(23,   "The Black Shroud",     152,  "East Shroud"),
        new(23,   "The Black Shroud",     153,  "South Shroud"),
        new(23,   "The Black Shroud",     154,  "North Shroud"),
        new(24,   "Thanalan",             140,  "Western Thanalan"),
        new(24,   "Thanalan",             141,  "Central Thanalan"),
        new(24,   "Thanalan",             145,  "Eastern Thanalan"),
        new(24,   "Thanalan",             146,  "Southern Thanalan"),
        new(24,   "Thanalan",             147,  "Northern Thanalan"),
        new(25,   "Coerthas",             155,  "Coerthas Central Highlands"),
        new(25,   "Coerthas",             397,  "Coerthas Western Highlands"),
        new(26,   "Mor Dhona",            156,  "Mor Dhona"),
        new(497,  "Abalathia's Spine",    401,  "The Sea of Clouds"),
        new(497,  "Abalathia's Spine",    402,  "Azys Lla"),
        new(498,  "Dravania",             398,  "The Dravanian Forelands"),
        new(498,  "Dravania",             399,  "The Dravanian Hinterlands"),
        new(498,  "Dravania",             400,  "The Churning Mists"),
        new(2400, "Gyr Abania",           612,  "The Fringes"),
        new(2400, "Gyr Abania",           620,  "The Peaks"),
        new(2400, "Gyr Abania",           621,  "The Lochs"),
        new(2401, "Othard",               613,  "The Ruby Sea"),
        new(2401, "Othard",               614,  "Yanxia"),
        new(2401, "Othard",               622,  "The Azim Steppe"),
        new(2950, "Norvrandt",            813,  "Lakeland"),
        new(2950, "Norvrandt",            814,  "Kholusia"),
        new(2950, "Norvrandt",            815,  "Amh Araeng"),
        new(2950, "Norvrandt",            816,  "Il Mheg"),
        new(2950, "Norvrandt",            817,  "The Rak'tika Greatwood"),
        new(2950, "Norvrandt",            818,  "The Tempest"),
        new(3702, "The Northern Empty",   956,  "Labyrinthos"),
        new(3703, "Ilsabard",             957,  "Thavnair"),
        new(3703, "Ilsabard",             958,  "Garlemald"),
        new(3704, "The Sea of Stars",     959,  "Mare Lamentorum"),
        new(3704, "The Sea of Stars",     960,  "Ultima Thule"),
        new(3705, "The World Unsundered", 961,  "Elpis"),
        new(4500, "Yok Tural",            1187, "Urqopacha"),
        new(4500, "Yok Tural",            1188, "Kozama'uka"),
        new(4500, "Yok Tural",            1189, "Yak T'el"),
        new(4501, "Xak Tural",            1190, "Shaaloani"),
        new(4501, "Xak Tural",            1191, "Heritage Found"),
        new(4502, "Unlost World",         1192, "Living Memory"),
    ];

    /// <summary>
    ///   Static readonly lookup keyed by the territory ID.
    /// </summary>
    public static IReadOnlyDictionary<ushort, PlaceRegionData> PlaceDataLookup { get; }
        = new ReadOnlyDictionary<ushort, PlaceRegionData>(PlaceData.ToDictionary(t => t.TerritoryId));

    // Should be using to ensure that a radar group cannot exist in housing zones! (May be missing exterior housing areas)
    public static readonly IReadOnlySet<ushort> HousingAreas = new HashSet<ushort>
    {
        282, 283, 284, 342, 343, 344, 345, 346, 347, 384, 385, 386, 649, 650, 651, 652, 980, 982, 983, // Houses
        608, 609, 610, 655, 999, // Apartments
    };

    /// <summary>
    ///   Helper to indicate which intended uses are forbidden in radar chat.
    /// </summary>
    public static readonly IReadOnlySet<IntendedUse> ForbiddenInChat = new HashSet<IntendedUse>()
    {
        // IntendedUse.Town, <-- Maybe uncomment later if nessisary.
        IntendedUse.MordionGaol,
        IntendedUse.HousingIndoor,
        IntendedUse.Frontline,
        IntendedUse.CrystallineConflict,
        IntendedUse.RivalWings,
    };

    public static string ToDisplayName(this IntendedUse use) => use switch
    {
        IntendedUse.Town => "Starting Towns",
        IntendedUse.Overworld => "Overworld",
        IntendedUse.Inn => "Inn",
        IntendedUse.Dungeon => "Dungeons",
        IntendedUse.VariantDungeon => "Variant Dungeons",
        IntendedUse.MordionGaol => "Mordion Gaol",
        IntendedUse.StartingArea => "Tutorial Areas",
        IntendedUse.QuestAreaBeforeTrialDungeon => "Pre-Content Quest Areas",
        IntendedUse.AllianceRaid => "Alliance Raids",
        IntendedUse.PreEwOverworldQuestBattle => "Quest Battles",
        IntendedUse.Trial => "Trials",
        IntendedUse.WaitingRoom => "Waiting Room",
        IntendedUse.HousingOutdoor => "Residential Areas",
        IntendedUse.HousingIndoor => "Indoor Housing",
        IntendedUse.SoloOverworldInstances => "Solo Instances",
        IntendedUse.Raid1 => "Raid Content A",
        IntendedUse.Raid2 => "Raid Content B",
        IntendedUse.Frontline => "Frontlines",
        IntendedUse.ChocoboSquareOld => "Chocobo Square (Old)",
        IntendedUse.ChocoboRacing => "Chocobo Racing",
        IntendedUse.Firmament => "The Firmament",
        IntendedUse.SanctumOfTheTwelve => "Sanctum of the Twelve",
        IntendedUse.GoldSaucer => "The Gold Saucer",
        IntendedUse.OriginalStepsOfFaith => "Steps of Faith (Old)",
        IntendedUse.LordOfVerminion => "Lord of Verminion",
        IntendedUse.ExploratoryMissions => "Exploratory Missions",
        IntendedUse.HallOfTheNovice => "Hall of the Novice",
        IntendedUse.CrystallineConflict or IntendedUse.CrystallineConflictCustomMatch => "Crystalline Conflict",
        IntendedUse.SoloDuty => "Solo Duties",
        IntendedUse.GrandCompanyBarracks => "Grand Company Barracks",
        IntendedUse.DeepDungeon => "Deep Dungeons",
        IntendedUse.Seasonal => "Seasonal Areas",
        IntendedUse.TreasureMapInstance => "Treasure Map Areas",
        IntendedUse.SeasonalInstancedArea => "Seasonal Instanced Area",
        IntendedUse.TripleTriadBattlehall => "Triple Triad",
        IntendedUse.ChaoticRaid => "Chaotic Raids",
        IntendedUse.HuntingGrounds => "Hunting Grounds",
        IntendedUse.RivalWings => "Rival Wings",
        IntendedUse.Eureka => "Eureka",
        IntendedUse.TheCalamityRetold => "The Calamity Retold",
        IntendedUse.LeapOfFaith => "Leap of Faith",
        IntendedUse.MaskedCarnival => "The Masked Carnival",
        IntendedUse.OceanFishing => "Ocean Fishing",
        IntendedUse.Diadem => "Diadem",
        IntendedUse.Bozja => "Bozja",
        IntendedUse.IslandSanctuary => "Island Sanctuary",
        IntendedUse.TripleTriadOpenTournament => "Triple Triad Tournament",
        IntendedUse.TripleTriadInvitationalParlor => "Triple Triad Parlor",
        IntendedUse.DelubrumReginae => "Delubrum Reginae",
        IntendedUse.DelubrumReginaeSavage => "Delubrum Reginae (Savage)",
        IntendedUse.EndwalkerMsqSoloOverworld => "Solo Instances (Endwalker)",
        IntendedUse.Elysion => "Elysion",
        IntendedUse.CriterionDungeon => "Criterion Dungeons",
        IntendedUse.CriterionDungeonSavage => "Criterion Dungeons (Savage)",
        IntendedUse.Blunderville => "Blunderville",
        IntendedUse.CosmicExploration => "Cosmic Explorations",
        IntendedUse.OccultCrescent => "Occult Crescent",

        // Unknown / unmapped cases
        IntendedUse.Unknown11 or
        IntendedUse.Unknown40 or
        IntendedUse.Unknown42 or
        IntendedUse.Unknown55 or
        IntendedUse.Unknown62 or
        IntendedUse.Unknown63 or
        IntendedUse.Unknown64 or
        IntendedUse.UNK => "Unknown",

        _ => "Unknown",
    };

    /// <summary>
    ///   Allow normal pairing over radar anywhere
    /// </summary>
    public static string RadarPublicKey(this LocationMeta loc)
        => $"{loc.WorldId}_{loc.TerritoryId}_{loc.WardId}_{loc.PlotOrDivisionId}_{loc.RoomId}";
    
    /// <summary>
    ///   Bar housing
    /// </summary>
    public static string RadarGroupKey(this LocationMeta loc)
        => $"{loc.WorldId}_{loc.TerritoryId}";

    /// <summary>
    ///   Gets the ChatlogID of the current location for a RadarChat identifier
    /// </summary>
    public static string? RadarChatKey(this LocationMeta loc)
    {
        var use = (IntendedUse)loc.IntendedUseId;
        // Under no condition should chat be accessible in restricted areas.
        // I do not want to be known as the person that allowed for open chat in pvp areas... lol.
        if (ForbiddenInChat.Contains(use))
            return null;

        return use switch
        {
            IntendedUse.Overworld => PlaceDataLookup.TryGetValue(loc.TerritoryId, out var placeData)
               ? $"{loc.DataCenterId}_{placeData.RegionName}" : null,
            _ => $"{loc.DataCenterId}_{use.ToDisplayName().Replace(' ', '_')}",
        };
    }
}
#nullable disable