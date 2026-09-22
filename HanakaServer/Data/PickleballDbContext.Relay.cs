using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Data;

public partial class PickleballDbContext
{
    public DbSet<RelayMatchScore> RelayMatchScores { get; set; }
    public DbSet<RelayTournamentSettings> RelayTournamentSettings { get; set; }
    public DbSet<RelayTeam> RelayTeams { get; set; }
    public DbSet<RelayTeamMember> RelayTeamMembers { get; set; }
    public DbSet<RelayTeamReserveMember> RelayTeamReserveMembers { get; set; }
    public DbSet<RelayMatchState> RelayMatchStates { get; set; }
    public DbSet<RelayLeg> RelayLegs { get; set; }
    public DbSet<RelayMatchCommand> RelayMatchCommands { get; set; }
    public DbSet<RelayBracketSeedSnapshot> RelayBracketSeedSnapshots { get; set; }
    public DbSet<RelayMatchLineupSnapshot> RelayMatchLineupSnapshots { get; set; }

    private static void ConfigureRelay(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RelayMatchScore>(e =>
        {
            e.ToTable("RelayMatchScores", t => t.HasCheckConstraint("CK_RelayMatchScores_Values",
                "[Part1Team1] >= 0 AND [Part1Team2] >= 0 AND [Part2Team1] >= 0 AND [Part2Team2] >= 0 AND [Part3Team1] >= 0 AND [Part3Team2] >= 0 AND [Version] > 0"));
            e.HasKey(x => x.MatchId);
            e.Property(x => x.MatchId).ValueGeneratedNever();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<TournamentGroupMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RelayTeamReserveMember>(e =>
        {
            e.ToTable("RelayTeamReserveMembers", t =>
            {
                t.HasCheckConstraint("CK_RelayReserves_Position", "[Position] BETWEEN 1 AND 4");
                t.HasCheckConstraint("CK_RelayReserves_Name", "LEN(LTRIM(RTRIM([DisplayName]))) > 0");
            });
            e.HasKey(x => new { x.RegistrationId, x.Position });
            e.Property(x => x.Position).HasColumnType("int");
            e.Property(x => x.DisplayName).HasMaxLength(150);
            e.Property(x => x.AvatarUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.RegistrationId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Team).WithMany(x => x.ReserveMembers).HasForeignKey(x => x.RegistrationId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayMatchLineupSnapshot>(e =>
        {
            e.ToTable("RelayMatchLineupSnapshots", t =>
            {
                t.HasCheckConstraint("CK_RelayMatchLineupSnapshots_Side", "[Side] IN (1,2)");
                t.HasCheckConstraint("CK_RelayMatchLineupSnapshots_Json", "ISJSON([LineupJson]) = 1");
            });
            e.HasKey(x => new { x.MatchId, x.Side });
            // The SQL script stores Side as tinyint; application code uses int for side keys.
            e.Property(x => x.Side).HasConversion<byte>().HasColumnType("tinyint");
            e.Property(x => x.TeamName).HasMaxLength(150);
            e.HasIndex(x => x.RegistrationId);
            e.HasOne<TournamentGroupMatch>().WithMany().HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RelayBracketSeedSnapshot>(e =>
        {
            e.ToTable("RelayBracketSeedSnapshots", t =>
            {
                t.HasCheckConstraint("CK_RelayBracketSeed_Number", "[SeedNumber] > 0");
                t.HasCheckConstraint("CK_RelayBracketSeed_Json", "ISJSON([LineupJson]) = 1");
            });
            e.HasKey(x => new { x.TournamentBracketApplicationId, x.SeedNumber });
            e.Property(x => x.TeamName).HasMaxLength(150);
            e.HasOne<TournamentBracketApplication>().WithMany().HasForeignKey(x => x.TournamentBracketApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayTournamentSettings>(e =>
        {
            e.ToTable("RelayTournamentSettings", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_RelaySettings_TeamSize", "[TeamSize] IN (4,6,8)");
                t.HasCheckConstraint("CK_RelaySettings_Rules", "[TargetScore] > 0 AND [LegDurationSeconds] > 0 AND [Version] >= 0");
            });
            e.HasKey(x => x.TournamentId);
            e.Property(x => x.TournamentId).ValueGeneratedNever();
            e.Property(x => x.DeadlinePolicy).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayTeam>(e =>
        {
            e.ToTable("RelayTeams", t =>
            {
                t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_RelayTeams_Version", "[Version] >= 0");
            });
            e.HasKey(x => x.RegistrationId);
            e.Property(x => x.RegistrationId).ValueGeneratedNever();
            e.Property(x => x.TeamName).HasMaxLength(150);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.TournamentId);
            e.HasOne<TournamentRegistration>().WithMany().HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RelayTournamentSettings>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CaptainUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayTeamMember>(e =>
        {
            e.ToTable("RelayTeamMembers", t =>
            {
                t.UseSqlOutputClause(false); // Retained for compatibility with databases that still have relay triggers.
                t.HasCheckConstraint("CK_RelayMembers_Position", "[Position] BETWEEN 1 AND 8");
            });
            e.HasKey(x => new { x.RegistrationId, x.Position });
            e.Property(x => x.DisplayName).HasMaxLength(150);
            e.Property(x => x.AvatarUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.RegistrationId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            e.HasOne(x => x.Team).WithMany(x => x.Members).HasForeignKey(x => x.RegistrationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayMatchState>(e =>
        {
            e.ToTable("RelayMatchStates", t =>
            {
                t.HasCheckConstraint("CK_RelayMatch_Rules", "[PairCount] IN (2,3,4) AND [TargetScore] > 0 AND [LegDurationSeconds] > 0 AND [Version] >= 0 AND [CurrentLegNumber] >= 0");
                t.HasCheckConstraint("CK_RelayMatch_Teams", "[Team1RegistrationId] <> [Team2RegistrationId]");
                t.HasCheckConstraint("CK_RelayMatch_Status", "[Status] IN ('READY','RUNNING','AWAITING_CHANGE','COMPLETED')");
            });
            e.HasKey(x => x.MatchId);
            e.Property(x => x.MatchId).ValueGeneratedNever();
            e.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.DeadlinePolicy).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<TournamentGroupMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RelayTournamentSettings>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RelayTeam>().WithMany().HasForeignKey(x => x.Team1RegistrationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RelayTeam>().WithMany().HasForeignKey(x => x.Team2RegistrationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayLeg>(e =>
        {
            e.ToTable("RelayLegs", t =>
            {
                t.HasCheckConstraint("CK_RelayLeg_Order", "[LegNumber] > 0 AND [PairNumber] BETWEEN 1 AND 4");
                t.HasCheckConstraint("CK_RelayLeg_Time", "[EndsAtUtc] > [StartedAtUtc] AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc] >= [StartedAtUtc])");
                t.HasCheckConstraint("CK_RelayLeg_Scores", "[StartScoreTeam1] >= 0 AND [StartScoreTeam2] >= 0 AND ([EndScoreTeam1] IS NULL OR [EndScoreTeam1] >= [StartScoreTeam1]) AND ([EndScoreTeam2] IS NULL OR [EndScoreTeam2] >= [StartScoreTeam2])");
            });
            e.HasKey(x => new { x.MatchId, x.LegNumber });
            e.HasOne(x => x.MatchState).WithMany(x => x.Legs).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RelayMatchCommand>(e =>
        {
            e.ToTable("RelayMatchCommands");
            e.HasKey(x => new { x.MatchId, x.RequestId });
            e.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.Operation).HasMaxLength(40).IsUnicode(false);
            e.HasIndex(x => new { x.MatchId, x.ResultVersion }).IsUnique();
            e.HasOne<RelayMatchState>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
