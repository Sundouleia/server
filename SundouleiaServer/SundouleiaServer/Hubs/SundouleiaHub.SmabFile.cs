using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using SundouleiaShared.Models;

namespace SundouleiaServer.Hubs;

public partial class SundouleiaHub
{
    // Used to attempt obtaining access to a file.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse<SMABFileInfo>> AccessFile(SMABFileAccess dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.AsNoTracking().SingleOrDefaultAsync(f => f.DataHash == dto.FileDataHash).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt<SMABFileInfo>(SundouleiaApiEc.NullData);

        // Fail if a password is set and is not matched.
        if (!string.IsNullOrEmpty(file.Password) && !string.Equals(file.Password, dto.Password, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt<SMABFileInfo>(SundouleiaApiEc.InvalidPassword);

        // Fail if the caller is not in the allowed access.
        var allowedUids = file.AllowedUIDsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!allowedUids.Contains(UserUID, StringComparer.Ordinal))
            return HubResponseBuilder.AwDangIt<SMABFileInfo>(SundouleiaApiEc.RecipientBlocked);

        // Return the file info to the valid user.
        var allowedHashes = file.AllowedHashesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return HubResponseBuilder.Yippee(new SMABFileInfo(file.EncryptedFileKey, allowedHashes));
    }

    // If we know the ID, we had access to the file.
    [Authorize(Policy = "Identified")] // Could maybe move to anonymous, unsure.
    public async Task<HubResponse<List<string>>> GetAllowedHashes(Guid FileId)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.AsNoTracking().SingleOrDefaultAsync(f => f.FileId == FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt<List<string>>(SundouleiaApiEc.NullData);
        // We can assume that this individual knows what the file is and as such does not need access to get allowed hashes.
        return HubResponseBuilder.Yippee(file.AllowedHashesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList());
    }

    // Creates a new protected SMAB file entry for a encrypted SMAB file an owner intends to share.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> CreateProtectedSMAB(NewSMABFile dto)
    {
        // If any details about this creation are invalid, we should fail before running any db checks.
        if (string.IsNullOrEmpty(dto.FileKey) || Guid.Empty == dto.FileId)
            return HubResponseBuilder.AwDangIt<SMABFileKey>(SundouleiaApiEc.NullData);

        // Fail if the dataHash is already owned by another user, or this SMAB fileId is already in use.
        if (await DbContext.ProtectedSMAFiles.AsNoTracking().AnyAsync(f => f.FileId == dto.FileId || f.DataHash == dto.EncryptedFileHash).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<SMABFileKey>(SundouleiaApiEc.AlreadyExists);

        // The Hash does not exist, however, if it is in the stale data table, it must belong to the caller. Otherwise, fail.
        if (await DbContext.ProtectedSMAFiles.AsNoTracking().AnyAsync(f => f.DataHash == dto.EncryptedFileHash && f.OwnerUID != UserUID).ConfigureAwait(false))
            return HubResponseBuilder.AwDangIt<SMABFileKey>(SundouleiaApiEc.RecipientBlocked);

        // Should do some other check here to get modified data hashes or whatever.

        // Construct the new protected file entry.
        var newEntry = new SMABaseFileData()
        {
            FileId = dto.FileId,
            OwnerUID = UserUID,
            DataHash = dto.EncryptedFileHash,
            EncryptedFileKey = dto.FileKey,
            Password = dto.Password ?? string.Empty,
            AllowedHashesCsv = string.Join(',', dto.AllowedHashes),
            AllowedUIDsCsv = string.Join(',', dto.AllowedUids),
            ExpireTime = dto.ExpireTime
        };
        // Add and save the new entry.
        await DbContext.ProtectedSMAFiles.AddAsync(newEntry).ConfigureAwait(false);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    // The SMABase File had its contents modified, and needs to be updated with the new FileDataHash, as it is still linked to the same FileId.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UpdateFileDataHash(SMABDataUpdate dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == dto.FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);

        // Update the data hash to the new value.
        file.DataHash = dto.NewData;
        DbContext.ProtectedSMAFiles.Update(file);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UpdateFilePassword(SMABDataUpdate dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == dto.FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);

        // Update the password to the new value.
        file.Password = dto.NewData;
        DbContext.ProtectedSMAFiles.Update(file);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UpdateAllowedHashes(SMABAccessUpdate dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == dto.FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);

        // Parse existing hashes into a HashSet for fast lookup.
        var allowedSet = new HashSet<string>(file.AllowedHashesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        // Add new hashes and remove unwanted ones.
        allowedSet.UnionWith(dto.ToAdd);
        allowedSet.ExceptWith(dto.ToRemove);
        // Store back as CSV.
        file.AllowedHashesCsv = string.Join(",", allowedSet);
        // Update and save.
        DbContext.ProtectedSMAFiles.Update(file);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UpdateAllowedUids(SMABAccessUpdate dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == dto.FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);

        // Parse existing UIDs into a HashSet for fast lookup.
        var allowedSet = new HashSet<string>(file.AllowedUIDsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
        // Add new UIDs and remove unwanted ones.
        allowedSet.UnionWith(dto.ToAdd);
        allowedSet.ExceptWith(dto.ToRemove);
        // Store back as CSV.
        file.AllowedUIDsCsv = string.Join(",", allowedSet);
        // Update and save.
        DbContext.ProtectedSMAFiles.Update(file);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> UpdateExpireTime(SMABExpireTime dto)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == dto.FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);

        // If the expire time is past the current time, remove the file.
        if (dto.NewTimeUtc <= DateTime.UtcNow)
        {
            DbContext.ProtectedSMAFiles.Remove(file);
        }
        else
        {
            // Otherwise, update the expire time, save, and return.
            file.ExpireTime = dto.NewTimeUtc;
            DbContext.ProtectedSMAFiles.Update(file);
        }

        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }

    // Must be authorized for this.
    [Authorize(Policy = "Identified")]
    public async Task<HubResponse> RemoveProtectedFile(Guid FileId)
    {
        // Fail if the file does not exist.
        if (await DbContext.ProtectedSMAFiles.SingleOrDefaultAsync(f => f.FileId == FileId).ConfigureAwait(false) is not { } file)
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NullData);

        // Fail if we are not the owner of the file.
        if (!string.Equals(file.OwnerUID, UserUID, StringComparison.Ordinal))
            return HubResponseBuilder.AwDangIt(SundouleiaApiEc.NotFileOwner);
        
        // Remove the file and save.
        DbContext.ProtectedSMAFiles.Remove(file);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        return HubResponseBuilder.Yippee();
    }
}

