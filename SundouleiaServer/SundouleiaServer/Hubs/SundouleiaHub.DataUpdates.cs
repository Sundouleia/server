using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SundouleiaAPI.Data;
using SundouleiaAPI.Enums;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaServer.Utils;
using SundouleiaShared.Metrics;

namespace SundouleiaServer.Hubs;
#nullable enable

public partial class SundouleiaHub
{
    // Pushes an update to mod and non-mod visual data to online pairs.
    // Currently non-functional.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<VerifiedModFile>>> UserPushIpcFull(PushIpcFull dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID).ToList();
        _logger.LogCallInfo(SundouleiaHubLogger.Args(recipientUids.Count));

        // Request the download links for all of the new files to be added.
        var requestResult = await RequestFiles(dto.Mods.FilesToAdd).ConfigureAwait(false);
        
        // compose the new return dto based off the resulting request.
        var newModUpdatesDto = new NewModUpdates(requestResult.DownloadFiles, dto.Mods.HashesToRemove, requestResult.RequiresUpload.Count);
        await Clients.Users(recipientUids).Callback_IpcUpdateFull(new(new(UserUID), newModUpdatesDto, dto.Visuals, dto.IsInitialData)).ConfigureAwait(false);
        
        // Inc metrics and return the remaining files to be uploaded to the server.
        _metrics.IncCounter(MetricsAPI.CounterDataUpdateAll);
        return HubResponseBuilder.Yippee(requestResult.RequiresUpload);
    }

    // Pushes an update to all mod visual data to online pairs.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<List<VerifiedModFile>>> UserPushIpcMods(PushIpcMods dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID).ToList();
        _logger.LogCallInfo(SundouleiaHubLogger.Args(recipientUids.Count));

        // Request the download links for all of the new files to be added.
        var requestResult = await RequestFiles(dto.Mods.FilesToAdd).ConfigureAwait(false);

        // compose the new return dto based off the resulting request.
        var newModUpdatesDto = new NewModUpdates(requestResult.DownloadFiles, dto.Mods.HashesToRemove, requestResult.RequiresUpload.Count);
        await Clients.Users(recipientUids).Callback_IpcUpdateMods(new(new(UserUID), newModUpdatesDto, dto.ManipString)).ConfigureAwait(false);

        // Inc metrics and return the remaining files to be uploaded to the server.
        _metrics.IncCounter(MetricsAPI.CounterDataUpdateMods);
        return HubResponseBuilder.Yippee(requestResult.RequiresUpload);
    }

    // Updates all non-mod visuals.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushIpcOther(PushIpcOther dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_IpcUpdateOther(new(new(UserUID), dto.Visuals)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterDataUpdateOther);
        return HubResponseBuilder.Yippee();
    }

    // Updates a single non-mod visual alteration.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushIpcSingle(PushIpcSingle dto)
    {
        // Hide this after we finish debugging.
        _logger.LogCallInfo(SundouleiaHubLogger.Args(dto));
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_IpcUpdateSingle(new(new(UserUID), dto.Object, dto.Kind, dto.NewData)).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterDataUpdateSingle);
        return HubResponseBuilder.Yippee();
    }

    #region M O O D L E S
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushMoodlesData(PushMoodlesData dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_PairMoodleDataUpdated(new(new(UserUID), dto.Data)).ConfigureAwait(false);
        return HubResponseBuilder.Yippee(); // No metrics yet.
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushMoodlesStatuses(PushMoodlesStatuses dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_PairMoodleStatusesUpdate(new(new(UserUID), dto.Statuses)).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushMoodlesPresets(PushMoodlesPresets dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_PairMoodlePresetsUpdate(new(new(UserUID), dto.Presets)).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushStatusModified(PushStatusModified dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_PairMoodleStatusModified(new(new(UserUID), dto.Status, dto.Deleted)).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserPushPresetModified(PushPresetModified dto)
    {
        var recipientUids = dto.Recipients.Select(r => r.UID);
        await Clients.Users(recipientUids).Callback_PairMoodlePresetModified(new(new(UserUID), dto.Preset, dto.Deleted)).ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }
    #endregion M O O D L E S

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserUpdateProfileContent(ProfileContent dto)
    {
        _logger.LogMessage($"ProfileContentUpdate:{UserUID}");

        // Prevent updates if the caller does not have a UserProfileData entry (should help with debugging)
        if (await DbContext.UserProfileData.SingleOrDefaultAsync(u => u.UserUID == UserUID).ConfigureAwait(false) is not { } callerProfile)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Update the profile data to their new values.
        callerProfile.UpdateInfoFromDto(dto);
        DbContext.Update(callerProfile);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform all online pairs of the caller to update the callers profile.
        var pairsOfCaller = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var onlinePairsOfCaller = await GetOnlineUsers(pairsOfCaller).ConfigureAwait(false);
        IEnumerable<string> onlineUids = onlinePairsOfCaller.Keys;

        await Clients.Users(onlineUids).Callback_ProfileUpdated(new(new(UserUID))).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterProfileUpdates);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UserUpdateProfilePicture(ProfileImage dto)
    {
        _logger.LogMessage($"ProfileImageUpdate:{UserUID}");

        // Prevent updates if the data does not exist to begin with.
        if (await DbContext.UserProfileData.SingleOrDefaultAsync(u => u.UserUID == UserUID).ConfigureAwait(false) is not { } existingProfile)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Validate the image if we are trying to set it. If the image is null/empty, it implies we are clearing the image.
        if (!string.IsNullOrEmpty(dto.NewBase64Image))
        {
            // Convert from the base64 into raw image data bytes.
            byte[] imageData = Convert.FromBase64String(dto.NewBase64Image);
            // Load the image into a memory stream
            using MemoryStream ms = new(imageData);
            // Detect format of the image (ensure valid png)
            SixLabors.ImageSharp.Formats.IImageFormat format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
            if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
            {
                await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "Provided Image must be PNG format").ConfigureAwait(false);
                return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidImageFormat);
            }

            // Temp load image into memory for FileSize and dimension checks.
            using Image<Rgba32> image = Image.Load<Rgba32>(imageData);
            if (image.Width > 256 || image.Height > 256)
            {
                await Clients.Caller.Callback_ServerMessage(MessageSeverity.Error, "Dimensions are larger than 256x256").ConfigureAwait(false);
                return HubResponseBuilder.AwDangIt(SundouleiaApiEc.InvalidImageSize);
            }
        }

        // Update the profile data entry and save changes.
        existingProfile.Base64AvatarData = dto.NewBase64Image;
        DbContext.Update(existingProfile);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);

        // Inform all online pairs of the caller to update the callers profile.
        var pairsOfCaller = await GetPairedUnpausedUsers().ConfigureAwait(false);
        var onlinePairsOfCaller = await GetOnlineUsers(pairsOfCaller).ConfigureAwait(false);
        IEnumerable<string> onlineUids = onlinePairsOfCaller.Keys;

        // Inform the client caller and all their pairs that their profile has been updated.
        await Clients.Users(onlineUids).Callback_ProfileUpdated(new(new(UserUID))).ConfigureAwait(false);
        _metrics.IncCounter(MetricsAPI.CounterProfileUpdates);
        return HubResponseBuilder.Yippee();
    }
}

