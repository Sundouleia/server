using SundouleiaShared.Models;
using Microsoft.EntityFrameworkCore;
#pragma warning disable MA0051 // Method is too long
namespace SundouleiaShared.Data;

public class SundouleiaDbContext : DbContext
{
#if DEBUG
    public SundouleiaDbContext()
    { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            base.OnConfiguring(optionsBuilder);
            return;
        }

        optionsBuilder.UseNpgsql("Host=51.222.107.111;Port=5432;Database=sundouleia;Username=cksundouleia", builder =>
        {
            builder.MigrationsHistoryTable("_efmigrationshistory", "public");
            builder.MigrationsAssembly("SundouleiaShared");
        }).UseSnakeCaseNamingConvention();
        optionsBuilder.EnableThreadSafetyChecks(false);

        base.OnConfiguring(optionsBuilder);
    }
#endif
    public SundouleiaDbContext(DbContextOptions<SundouleiaDbContext> options) : base(options) { }

    // Account Handling.
    public DbSet<AccountClaimAuth> AccountClaimAuth { get; set; }
    public DbSet<Auth> Auth { get; set; }
    public DbSet<AccountReputation> AccountReputation { get; set; }

    // Handling Naughy users.
    public DbSet<BannedRegistrations> BannedRegistrations { get; set; }
    public DbSet<Banned> BannedUsers { get; set; }
    public DbSet<BannedSanctionUser> BannedSanctionUsers { get; set; }
    public DbSet<BlacklistedUser> BlacklistedUsers { get; set; }

    // Pair Control
    public DbSet<ClientPair> ClientPairs { get; set; }
    public DbSet<ClientPairPermissions> ClientPairPerms { get; set; }
    public DbSet<PairRequest> Requests { get; set; }

    // Sanctioned Groups
    public DbSet<SanctionedGroup> SanctionedGroups { get; set; }
    public DbSet<SanctionPair> SanctionPairs { get; set; }
    public DbSet<SanctionRole> SanctionRoles { get; set; }
    public DbSet<SanctionOwnership> SanctionOwnerships { get; set; }
    public DbSet<SanctionProfile> SanctionProfiles { get; set; }

    // Reports
    public DbSet<ReportedUserProfile> ProfileReports { get; set; }
    public DbSet<ReportedRadar> RadarReports { get; set; }

    // User Information
    public DbSet<User> Users { get; set; }
    public DbSet<GlobalPermissions> UserGlobalPerms { get; set; }
    public DbSet<UserProfileData> UserProfileData { get; set; }

    // File Security (For those who want it)
    public DbSet<SMABaseFileData> ProtectedSMAFiles { get; set; }

    // We can add a table to track stale entries here if necessary, but for the moment there is not much reason to do this.
    // A person cannot claim a data hash that was changed and no longer exists since it is still encrypted and they won't know the decryption key for it.
    // Likewise, the only way they would have this data is if it was extracting and in use during the time that this access was changed.
    // At this point, the issue would narrow down to the level of trust the owner had when sharing the file, and how much time they let it be shared for.
    //
    // Ultimately you can never fully prevent access from an unwanted source, but this barrier allows for a layer of safety people may want to use.
    // At the end of the day, know whoever you share the base file with has full access to it.

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Account Handling.
        mb.Entity<AccountClaimAuth>().ToTable("account_claim_auth");
        mb.Entity<Auth>().ToTable("auth");
        mb.Entity<Auth>().HasIndex(a => a.UserUID);
        mb.Entity<Auth>().HasIndex(a => a.PrimaryUserUID);
        mb.Entity<AccountReputation>().ToTable("account_reputation");

        // Handling Naughy users.
        mb.Entity<Banned>().ToTable("banned_users");
        mb.Entity<BannedRegistrations>().ToTable("banned_registrations");
        mb.Entity<BannedSanctionUser>().ToTable("banned_sanction_users");
        mb.Entity<BannedSanctionUser>().Property(g => g.BannedAtUTC).HasDefaultValueSql("CURRENT_TIMESTAMP");
        mb.Entity<BlacklistedUser>().ToTable("blacklisted_users");
        mb.Entity<BlacklistedUser>().HasKey(u => new { u.UserUID, u.BlockedUserUID });
        mb.Entity<BlacklistedUser>().HasIndex(c => c.UserUID);
        mb.Entity<BlacklistedUser>().HasIndex(c => c.BlockedUserUID);

        // Pair Control
        mb.Entity<ClientPair>().ToTable("client_pairs");
        mb.Entity<ClientPair>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        mb.Entity<ClientPair>().HasIndex(c => c.UserUID);
        mb.Entity<ClientPair>().HasIndex(c => c.OtherUserUID);
        mb.Entity<ClientPair>().HasIndex(c => c.TempAccepterUID);
        mb.Entity<ClientPairPermissions>().ToTable("client_pair_permissions");
        mb.Entity<ClientPairPermissions>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        mb.Entity<ClientPairPermissions>().HasIndex(c => c.UserUID);
        mb.Entity<ClientPairPermissions>().HasIndex(c => c.OtherUserUID);
        mb.Entity<PairRequest>().ToTable("pair_requests");
        mb.Entity<PairRequest>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        mb.Entity<PairRequest>().HasIndex(c => c.UserUID);
        mb.Entity<PairRequest>().HasIndex(c => c.OtherUserUID);

        // Sanctioned Groups
        mb.Entity<SanctionedGroup>().ToTable("sanctioned_groups");
        mb.Entity<SanctionedGroup>().HasIndex(c => c.OwnerUID);
        mb.Entity<SanctionedGroup>().HasIndex(c => c.EstateHouseID);
        mb.Entity<SanctionedGroup>().HasIndex(c => c.ChatlogId);
        mb.Entity<SanctionedGroup>().Property(g => g.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        // Ensure the SanctionedGroup profile is removed when it is deleted
        mb.Entity<SanctionedGroup>().HasOne(g => g.Profile)
            .WithOne(p => p.Sanction)
            .HasForeignKey<SanctionProfile>(p => p.SanctionID)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<SanctionedGroup>() // Ensure the SanctionedGroups roles are removed when it is deleted.
            .HasMany(x => x.Roles)
            .WithOne(r => r.Sanction)
            .HasForeignKey(r => r.SanctionID)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<SanctionPair>().ToTable("sanction_pairs");
        mb.Entity<SanctionPair>().HasIndex(c => c.SanctionID);
        mb.Entity<SanctionPair>().HasIndex(c => c.SanctionUserUID);
        mb.Entity<SanctionPair>().Property(p => p.JoinedAtUTC).HasDefaultValueSql("CURRENT_TIMESTAMP");

        mb.Entity<SanctionRole>().ToTable("sanction_roles");
        mb.Entity<SanctionRole>().HasIndex(c => c.SanctionID);
        mb.Entity<SanctionRole>().HasIndex(c => c.Name);

        mb.Entity<SanctionOwnership>().ToTable("sanction_ownerships");
        mb.Entity<SanctionOwnership>().HasIndex(c => c.ApartmentHouseID);
        mb.Entity<SanctionOwnership>().HasIndex(c => c.PersonalHouseID);
        mb.Entity<SanctionOwnership>().HasIndex(c => c.FreeCompanyHouseID);

        mb.Entity<SanctionProfile>().ToTable("sanction_profiles");
        mb.Entity<SanctionProfile>().HasIndex(c => c.SanctionID);
        mb.Entity<SanctionProfile>().HasIndex(c => c.GroupTags);

        // Reports
        mb.Entity<ReportedUserProfile>().ToTable("reported_profiles");
        mb.Entity<ReportedUserProfile>().HasIndex(c => c.ReportedUserUID);
        mb.Entity<ReportedUserProfile>().HasIndex(c => c.ReportingUserUID);
        mb.Entity<ReportedRadar>().ToTable("reported_radars");
        mb.Entity<ReportedRadar>().HasIndex(c => c.Kind);
        mb.Entity<ReportedRadar>().HasIndex(c => c.WorldId);
        mb.Entity<ReportedRadar>().HasIndex(c => c.TerritoryId);
        mb.Entity<ReportedRadar>().HasIndex(c => c.ReporterUID);

        // User Information
        mb.Entity<User>().ToTable("users");
        mb.Entity<GlobalPermissions>().ToTable("user_global_permissions");
        mb.Entity<GlobalPermissions>().HasIndex(c => c.UserUID);
        mb.Entity<UserProfileData>().ToTable("user_profile");
        mb.Entity<UserProfileData>().HasIndex(c => c.UserUID);

        // File Security
        mb.Entity<SMABaseFileData>().ToTable("protected_sma_files");
        mb.Entity<SMABaseFileData>().HasKey(u => new { u.OwnerUID, u.FileId });
        mb.Entity<SMABaseFileData>().HasIndex(c => c.OwnerUID);
        mb.Entity<SMABaseFileData>().HasIndex(c => c.FileId);
        mb.Entity<SMABaseFileData>().HasIndex(c => c.DataHash);
    }
}