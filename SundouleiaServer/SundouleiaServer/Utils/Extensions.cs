using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using SundouleiaShared.Models;

namespace SundouleiaServer;
#nullable enable

public static class Extensions
{
    public static void UpdateInfoFromDto(this UserProfileData storedData, ProfileContent dto)
    {
        storedData.IsPublic = dto.IsPublic;

        storedData.AvatarVis = dto.AvatarVis;
        storedData.DescriptionVis = dto.DescriptionVis;
        storedData.DecorationVis = dto.DecorationVis;

        storedData.Description = dto.Description;

        storedData.MainBG = dto.MainBG;
        storedData.MainBorder = dto.MainBorder;

        storedData.AvatarBG = dto.AvatarBG;
        storedData.AvatarBorder = dto.AvatarBorder;
        storedData.AvatarOverlay = dto.AvatarOverlay;

        storedData.DescriptionBG = dto.DescriptionBG;
        storedData.DescriptionBorder = dto.DescriptionBorder;
        storedData.DescriptionOverlay = dto.DescriptionOverlay;
    }

    public static ProfileContent FromProfileData(this UserProfileData data)
        => new ProfileContent()
        {
            IsPublic = data.IsPublic,
            Flagged = data.FlaggedForReport,
            Disabled = data.IsDisabled,
            AvatarVis = data.AvatarVis,
            DescriptionVis = data.DescriptionVis,
            DecorationVis = data.DecorationVis,
            Description = data.Description,

            MainBG = data.MainBG,
            MainBorder = data.MainBorder,

            AvatarBG = data.AvatarBG,
            AvatarBorder = data.AvatarBorder,
            AvatarOverlay = data.AvatarOverlay,

            DescriptionBG = data.DescriptionBG,
            DescriptionBorder = data.DescriptionBorder,
            DescriptionOverlay = data.DescriptionOverlay
        };

    public static UserData ToUserData(this User user)
        => new UserData(user.UID, user.Alias, user.Tier, user.CreatedAt);

    public static SundesmoRequest ToApi(this PairRequest request)
        => new SundesmoRequest(new(request.UserUID), new(request.OtherUserUID), request.IsTemporary, request.AttachedMessage, request.CreationTime);

    public static SundesmoRequest ToApiRemoval(UserData user, UserData target) =>
        new(user, target, false, string.Empty, DateTime.MinValue);

    public static GlobalPerms ToApi(this GlobalPermissions? dbState)
        => dbState is null ? new() : new GlobalPerms()
        {
            DefaultAllowAnimations = dbState.DefaultAllowAnimations,
            DefaultAllowSounds = dbState.DefaultAllowSounds,
            DefaultAllowVfx = dbState.DefaultAllowVfx,
        };

    public static PairPerms ToApi(this ClientPairPermissions? dbState)
        => dbState is null ? new() : new PairPerms()
        {
            PauseVisuals = dbState.PauseVisuals,
            AllowAnimations = dbState.AllowAnimations,
            AllowSounds = dbState.AllowSounds,
            AllowVfx = dbState.AllowVfx
        };

    public static UserReputation ToApi(this AccountReputation? dbState)
        => dbState is null ? new() : new UserReputation()
        {
            IsVerified = dbState.IsVerified,
            IsBanned = dbState.IsBanned,
            WarningStrikes = dbState.WarningStrikes,
            ProfileViewing = dbState.ProfileViewing,
            ProfileViewStrikes = dbState.ProfileViewStrikes,
            ProfileEditing = dbState.ProfileEditing,
            ProfileEditStrikes = dbState.ProfileEditStrikes,
            RadarUsage = dbState.RadarUsage,
            RadarStrikes = dbState.RadarStrikes,
            ChatUsage = dbState.ChatUsage,
            ChatStrikes = dbState.ChatStrikes
        };

    public static void UpdateDbModel(this GlobalPerms api, GlobalPermissions current)
    {
        if (api is null)
            return;

        current.DefaultAllowAnimations = api.DefaultAllowAnimations;
        current.DefaultAllowSounds = api.DefaultAllowSounds;
        current.DefaultAllowVfx = api.DefaultAllowVfx;
    }

    public static void UpdateDbModel(this PairPerms api, ClientPairPermissions current)
    {
        if (api is null)
            return;

        current.PauseVisuals = api.PauseVisuals;
        current.AllowAnimations = api.AllowAnimations;
        current.AllowSounds = api.AllowSounds;
        current.AllowVfx = api.AllowVfx;
    }
}
#nullable disable