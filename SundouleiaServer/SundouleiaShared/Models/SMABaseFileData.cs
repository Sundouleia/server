using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SundouleiaShared.Models;
#pragma warning disable CS8632

/// <summary>
///     Defines the internal information of a file by its ID or DataHash. <para />
///     A BaseFile can be modified by its owner at any point to update:
///     <list type="bullet">
///         <item>DataHash</item>
///         <item>EncryptedFileKey (When the dataHash gets changed) </item>
///         <item>Password (If they want to change it) </item>
///     </list>
/// </summary>
public class SMABaseFileData
{
    [Key]
    [Required]
    [Column(Order = 0)]
    public string OwnerUID { get; set; }

    [ForeignKey(nameof(OwnerUID))]
    public virtual User Owner { get; set; }

    // This should supposedly persist between updates to a file, but we will see.
    [Key]
    [Required]
    [Column(Order = 1)]
    public Guid FileId { get; set; }

    [Column(Order = 2)] // Should be updatable whenever we change the file's snapshot.
    public string DataHash { get; set; }

    [Column(Order = 3)] // Owner-Encrypted AES Key for the file.
    public string EncryptedFileKey { get; set; }

    [Column(Order = 4)] // Optional password for accessing the file.
    public string Password { get; set; } = string.Empty;

    // File Lifetime.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpireTime { get; set; } = DateTime.MaxValue;

    // The following is just to avoid dealing with ICollection entries flooding the database.
    // (Because this can create a nightmarish amount of entries like Syncshells did.
    // (It is not checked frequently outside file access, so this should be fine. We can do JSONB if not)
    public string AllowedHashesCsv { get; set; } = string.Empty; // CSV of allowed hashes.
    public string AllowedUIDsCsv { get; set; } = string.Empty; // CSV of allowed UIDs.
}
#pragma warning restore CS8632