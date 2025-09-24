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

        optionsBuilder.UseNpgsql("Host=167.71.108.254;Port=5432;Database=sundouleia;Username=cksundouleia", builder =>
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

    // Naughty users timeout corner management tables.
    public DbSet<Banned> BannedUsers { get; set; }
    public DbSet<BannedRegistrations> BannedRegistrations { get; set; }
    public DbSet<BlockedUser> BlockedUsers { get; set; }

    // Sundesmo management.
    public DbSet<ClientPair> ClientPairs { get; set; }
    public DbSet<ClientPairPermissions> ClientPairPerms { get; set; }

    // Requests
    public DbSet<PairRequest> Requests { get; set; }

    // Reports
    public DbSet<ReportedUserProfile> ProfileReports { get; set; }
    public DbSet<ReportedRadar> RadarReports { get; set; }

    // User Information
    public DbSet<User> Users { get; set; }
    public DbSet<GlobalPermissions> UserGlobalPerms { get; set; }
    public DbSet<UserProfileData> UserProfileData { get; set; }
    public DbSet<UserRadarInfo> UserRadarInfo { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountClaimAuth>().ToTable("account_claim_auth");
        modelBuilder.Entity<Auth>().ToTable("auth");
        modelBuilder.Entity<Auth>().HasIndex(a => a.UserUID);
        modelBuilder.Entity<Auth>().HasIndex(a => a.PrimaryUserUID);
        modelBuilder.Entity<AccountReputation>().ToTable("account_reputation");

        modelBuilder.Entity<Banned>().ToTable("banned_users");
        modelBuilder.Entity<BannedRegistrations>().ToTable("banned_registrations");
        modelBuilder.Entity<BlockedUser>().ToTable("blocked_users");
        modelBuilder.Entity<BlockedUser>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        modelBuilder.Entity<BlockedUser>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<BlockedUser>().HasIndex(c => c.OtherUserUID);

        modelBuilder.Entity<ClientPair>().ToTable("client_pairs");
        modelBuilder.Entity<ClientPair>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        modelBuilder.Entity<ClientPair>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<ClientPair>().HasIndex(c => c.OtherUserUID);
        modelBuilder.Entity<ClientPairPermissions>().ToTable("client_pair_permissions");
        modelBuilder.Entity<ClientPairPermissions>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        modelBuilder.Entity<ClientPairPermissions>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<ClientPairPermissions>().HasIndex(c => c.OtherUserUID);

        modelBuilder.Entity<PairRequest>().ToTable("pair_requests");
        modelBuilder.Entity<PairRequest>().HasKey(u => new { u.UserUID, u.OtherUserUID });
        modelBuilder.Entity<PairRequest>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<PairRequest>().HasIndex(c => c.OtherUserUID);

        modelBuilder.Entity<ReportedUserProfile>().ToTable("reported_profiles");
        modelBuilder.Entity<ReportedUserProfile>().HasIndex(c => c.ReportedUserUID);
        modelBuilder.Entity<ReportedUserProfile>().HasIndex(c => c.ReportingUserUID);
        modelBuilder.Entity<ReportedRadar>().ToTable("reported_radars");
        modelBuilder.Entity<ReportedRadar>().HasIndex(c => c.Kind);
        modelBuilder.Entity<ReportedRadar>().HasIndex(c => c.TerritoryID);
        modelBuilder.Entity<ReportedRadar>().HasIndex(c => c.ReporterUID);

        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<GlobalPermissions>().ToTable("user_global_permissions");
        modelBuilder.Entity<GlobalPermissions>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<UserProfileData>().ToTable("user_profile");
        modelBuilder.Entity<UserProfileData>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<UserRadarInfo>().ToTable("user_radar_info");
        modelBuilder.Entity<UserRadarInfo>().HasKey(u => new { u.UserUID, u.TerritoryId, u.WorldId });
        modelBuilder.Entity<UserRadarInfo>().HasIndex(c => c.UserUID);
        modelBuilder.Entity<UserRadarInfo>().HasIndex(c => c.TerritoryId);
        modelBuilder.Entity<UserRadarInfo>().HasIndex(c => c.WorldId);
    }
}