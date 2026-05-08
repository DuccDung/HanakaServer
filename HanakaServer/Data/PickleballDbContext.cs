using System;
using System.Collections.Generic;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Data;

public partial class PickleballDbContext : DbContext
{
    public PickleballDbContext(DbContextOptions<PickleballDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Banner> Banners { get; set; }

    public virtual DbSet<Club> Clubs { get; set; }

    public virtual DbSet<ClubMember> ClubMembers { get; set; }

    public virtual DbSet<ClubMessage> ClubMessages { get; set; }

    public virtual DbSet<ModerationReport> ModerationReports { get; set; }

    public virtual DbSet<Coach> Coaches { get; set; }

    public virtual DbSet<Court> Courts { get; set; }

    public virtual DbSet<CourtImage> CourtImages { get; set; }

    public virtual DbSet<Exchange> Exchanges { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Tournament> Tournaments { get; set; }

    public virtual DbSet<TournamentRegistration> TournamentRegistrations { get; set; }

    public virtual DbSet<TournamentPairRequest> TournamentPairRequests { get; set; }

    public virtual DbSet<TournamentRound> TournamentRounds { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserNotification> UserNotifications { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<TournamentRoundMap> TournamentRoundMaps { get; set; }
    public virtual DbSet<TournamentRoundGroup> TournamentRoundGroups { get; set; }
    public virtual DbSet<TournamentGroupMatch> TournamentGroupMatches { get; set; }
    public virtual DbSet<Referee> Referees { get; set; }
    public virtual DbSet<Link> Links { get; set; }
    public virtual DbSet<UserOtp> UserOtps { get; set; }
    public virtual DbSet<UserRatingHistory> UserRatingHistories { get; set; }
    public virtual DbSet<UserAchievement> UserAchievements { get; set; }
    public virtual DbSet<UserBlock> UserBlocks { get; set; }
    public virtual DbSet<TournamentPrize> TournamentPrizes { get; set; }
    public virtual DbSet<TournamentMatchScoreHistory> TournamentMatchScoreHistories { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModerationReport>(entity =>
        {
            entity.ToTable("ModerationReports");

            entity.HasKey(e => e.ReportId)
                .HasName("PK_ModerationReports");

            entity.HasIndex(e => new { e.Status, e.CreatedAt })
                .HasDatabaseName("IX_ModerationReports_Status_CreatedAt")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.ReporterUserId, e.CreatedAt })
                .HasDatabaseName("IX_ModerationReports_ReporterUserId_CreatedAt")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.TargetUserId, e.CreatedAt })
                .HasDatabaseName("IX_ModerationReports_TargetUserId_CreatedAt")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.ClubId, e.CreatedAt })
                .HasDatabaseName("IX_ModerationReports_ClubId_CreatedAt")
                .IsDescending(false, true);

            entity.HasIndex(e => e.MessageId)
                .HasDatabaseName("IX_ModerationReports_MessageId");

            entity.Property(e => e.ReportType)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.Property(e => e.ReasonCode)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.Property(e => e.ReasonLabel)
                .HasMaxLength(150);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.Property(e => e.ReporterNameSnapshot)
                .HasMaxLength(150);

            entity.Property(e => e.TargetNameSnapshot)
                .HasMaxLength(150);

            entity.Property(e => e.Source)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("CHAT");

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.Property(e => e.DeveloperNotified)
                .HasDefaultValue(false);

            entity.Property(e => e.DeveloperNotifiedAt)
                .HasPrecision(0);

            entity.Property(e => e.ReviewedByName)
                .HasMaxLength(150);

            entity.Property(e => e.ReviewedAt)
                .HasPrecision(0);

            entity.Property(e => e.ResolutionAction)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("NONE");

            entity.Property(e => e.ResolutionNote)
                .HasMaxLength(1000);

            entity.Property(e => e.ResolvedAt)
                .HasPrecision(0);

            entity.Property(e => e.SlaDueAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(dateadd(hour,(24),sysdatetime()))");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ModerationReports_ReportType",
                "[ReportType] IN ('USER', 'MESSAGE')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ModerationReports_ReasonCode",
                "[ReasonCode] IN ('HATE_OR_HARASSMENT', 'VIOLENT_THREAT', 'SEXUAL_CONTENT', 'SPAM_OR_SCAM', 'OTHER')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ModerationReports_Status",
                "[Status] IN ('PENDING', 'UNDER_REVIEW', 'RESOLVED', 'REJECTED')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ModerationReports_ResolutionAction",
                "[ResolutionAction] IN ('NONE', 'MESSAGE_HIDDEN', 'USER_WARNED', 'USER_EJECTED', 'USER_BANNED')"));

            entity.HasOne(d => d.ReporterUser)
                .WithMany(p => p.ModerationReportsReporter)
                .HasForeignKey(d => d.ReporterUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ModerationReports_ReporterUser");

            entity.HasOne(d => d.TargetUser)
                .WithMany(p => p.ModerationReportsTarget)
                .HasForeignKey(d => d.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ModerationReports_TargetUser");

            entity.HasOne(d => d.Club)
                .WithMany(p => p.ModerationReports)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ModerationReports_Club");

            entity.HasOne(d => d.Message)
                .WithMany(p => p.ModerationReports)
                .HasForeignKey(d => d.MessageId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ModerationReports_Message");

            entity.HasOne(d => d.ReviewedByUser)
                .WithMany(p => p.ModerationReportsReviewedBy)
                .HasForeignKey(d => d.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ModerationReports_ReviewedByUser");
        });

        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.ToTable("UserBlocks");

            entity.HasKey(e => e.BlockId)
                .HasName("PK_UserBlocks");

            entity.HasIndex(e => new { e.BlockerUserId, e.IsActive, e.BlockedAt })
                .HasDatabaseName("IX_UserBlocks_BlockerUserId_IsActive")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.BlockedUserId, e.IsActive, e.BlockedAt })
                .HasDatabaseName("IX_UserBlocks_BlockedUserId_IsActive")
                .IsDescending(false, false, true);

            entity.HasIndex(e => new { e.BlockerUserId, e.BlockedUserId })
                .IsUnique()
                .HasFilter("([IsActive]=(1))")
                .HasDatabaseName("UX_UserBlocks_ActivePair");

            entity.Property(e => e.ReasonCode)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.Property(e => e.Notes)
                .HasMaxLength(500);

            entity.Property(e => e.Source)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("CHAT");

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.BlockedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.UnblockedAt)
                .HasPrecision(0);

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_UserBlocks_NotSelf",
                "[BlockerUserId] <> [BlockedUserId]"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_UserBlocks_ReasonCode",
                "[ReasonCode] IN ('HATE_OR_HARASSMENT', 'VIOLENT_THREAT', 'SEXUAL_CONTENT', 'SPAM_OR_SCAM', 'OTHER')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_UserBlocks_Source",
                "[Source] IN ('CHAT', 'PROFILE', 'ADMIN', 'SYSTEM')"));

            entity.HasOne(d => d.BlockerUser)
                .WithMany(p => p.UserBlocksBlockedByMe)
                .HasForeignKey(d => d.BlockerUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserBlocks_BlockerUser");

            entity.HasOne(d => d.BlockedUser)
                .WithMany(p => p.UserBlocksBlockingMe)
                .HasForeignKey(d => d.BlockedUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserBlocks_BlockedUser");

            entity.HasOne(d => d.SourceClub)
                .WithMany(p => p.UserBlocks)
                .HasForeignKey(d => d.SourceClubId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserBlocks_SourceClub");

            entity.HasOne(d => d.SourceMessage)
                .WithMany(p => p.UserBlocks)
                .HasForeignKey(d => d.SourceMessageId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserBlocks_SourceMessage");

            entity.HasOne(d => d.Report)
                .WithMany(p => p.UserBlocks)
                .HasForeignKey(d => d.ReportId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserBlocks_Report");
        });

        modelBuilder.Entity<TournamentMatchScoreHistory>(entity =>
        {
            entity.ToTable("TournamentMatchScoreHistories");

            entity.HasKey(e => e.ScoreHistoryId);

            entity.HasIndex(e => e.MatchId)
                .HasDatabaseName("IX_TMSH_MatchId");

            entity.HasIndex(e => e.RefereeUserId)
                .HasDatabaseName("IX_TMSH_RefereeUserId");

            entity.HasIndex(e => new { e.MatchId, e.CreatedAt })
                .HasDatabaseName("IX_TMSH_Match_CreatedAt");

            entity.Property(e => e.Note)
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.ScoreTeam1)
                .HasDefaultValue(0);

            entity.Property(e => e.ScoreTeam2)
                .HasDefaultValue(0);

            entity.Property(e => e.IsCompleted)
                .HasDefaultValue(false);

            entity.HasOne(d => d.Match)
                .WithMany(p => p.TournamentMatchScoreHistories)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TMSH_Match");

            entity.HasOne(d => d.RefereeUser)
                .WithMany(p => p.RefereeScoreHistories)
                .HasForeignKey(d => d.RefereeUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TMSH_RefereeUser");

            entity.HasOne(d => d.WinnerRegistration)
                .WithMany()
                .HasForeignKey(d => d.WinnerRegistrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TMSH_WinnerRegistration");
        });
        modelBuilder.Entity<TournamentPrize>(entity =>
        {
            entity.ToTable("TournamentPrizes", "dbo");

            entity.HasKey(e => e.TournamentPrizeId)
                  .HasName("PK_TournamentPrizes");

            entity.HasIndex(e => new { e.TournamentId, e.PrizeType, e.PrizeOrder })
                  .IsUnique()
                  .HasDatabaseName("UX_TournamentPrizes_Tournament_PrizeType_PrizeOrder");

            entity.HasIndex(e => new { e.TournamentId, e.RegistrationId })
                  .IsUnique()
                  .HasDatabaseName("UX_TournamentPrizes_Tournament_Registration")
                  .HasFilter("[RegistrationId] IS NOT NULL");

            entity.HasIndex(e => e.TournamentId)
                  .HasDatabaseName("IX_TournamentPrizes_TournamentId");

            entity.HasIndex(e => e.RegistrationId)
                  .HasDatabaseName("IX_TournamentPrizes_RegistrationId");

            entity.Property(e => e.PrizeType)
                  .HasMaxLength(20)
                  .IsUnicode(false);

            entity.Property(e => e.PrizeOrder)
                  .HasDefaultValue(1);

            entity.Property(e => e.IsConfirmed)
                  .HasDefaultValue(false);

            entity.Property(e => e.Note)
                  .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                  .HasColumnType("datetime")
                  .HasDefaultValueSql("(sysdatetime())");

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_TournamentPrizes_PrizeType",
                "[PrizeType] IN ('FIRST', 'SECOND', 'THIRD')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_TournamentPrizes_PrizeOrder",
                "[PrizeOrder] >= 1"));

            entity.HasOne(d => d.Tournament)
                  .WithMany(p => p.TournamentPrizes)
                  .HasForeignKey(d => d.TournamentId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .HasConstraintName("FK_TournamentPrizes_Tournament");

            entity.HasOne(d => d.Registration)
                  .WithMany(p => p.TournamentPrizes)
                  .HasForeignKey(d => d.RegistrationId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_TournamentPrizes_Registration");
        });
        modelBuilder.Entity<UserAchievement>(entity =>
        {
            entity.ToTable("UserAchievements", "dbo");

            entity.HasKey(e => e.UserAchievementId).HasName("PK_UserAchievements");

            entity.HasIndex(e => e.UserId, "IX_UserAchievements_UserId");
            entity.HasIndex(e => e.TournamentId, "IX_UserAchievements_TournamentId");

            entity.Property(e => e.AchievementType)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.Note).HasMaxLength(500);

            entity.ToTable(t =>
                t.HasCheckConstraint("CK_UserAchievements_Type",
                    "[AchievementType] IN ('FIRST', 'SECOND', 'THIRD')"));

            entity.HasOne(d => d.User)
                .WithMany(p => p.UserAchievements)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserAchievements_User");

            entity.HasOne(d => d.Tournament)
                .WithMany(p => p.UserAchievements)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserAchievements_Tournament");
        });
        modelBuilder.Entity<UserRatingHistory>(entity =>
        {
            entity.ToTable("UserRatingHistory", "dbo");

            entity.HasKey(e => e.RatingHistoryId);

            entity.Property(e => e.RatingHistoryId)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.RatingSingle)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.RatingDouble)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Note)
                .HasMaxLength(500);

            entity.Property(e => e.RatedAt)
                .HasColumnType("datetime");

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRatingHistories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserRatingHistory_User");

            entity.HasOne(e => e.RatedByUser)
                .WithMany()
                .HasForeignKey(e => e.RatedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserRatingHistory_RatedByUser");

            entity.HasOne(e => e.Tournament)
                .WithMany()
                .HasForeignKey(e => e.TournamentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_UserRatingHistory_Tournament");
        });
        modelBuilder.Entity<UserOtp>(entity =>
        {
            entity.HasKey(e => e.UserOtpId).HasName("PK_UserOtp");

            entity.ToTable("UserOtp");

            entity.Property(e => e.UserOtpId).HasColumnName("user_otp_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.OtpCode)
                .HasMaxLength(10)
                .HasColumnName("otp_code");
            entity.Property(e => e.ExpiredAt)
                .HasColumnType("datetime")
                .HasColumnName("expired_at");
            entity.Property(e => e.IsUsed).HasColumnName("is_used");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.UsedAt)
                .HasColumnType("datetime")
                .HasColumnName("used_at");

            entity.HasOne(d => d.User)
                .WithMany(p => p.UserOtps)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserOtp_User");
        });
        modelBuilder.Entity<Link>(entity =>
        {
            entity.HasKey(e => e.LinkId);

            entity.ToTable("Links");

            entity.Property(e => e.LinkId)
                .HasColumnName("LinkId");

            entity.Property(e => e.Url)
                .HasColumnName("Link")
                .HasMaxLength(1000);

            entity.Property(e => e.Type)
                .HasMaxLength(50);
        });
        modelBuilder.Entity<Referee>(entity =>
        {
            entity.HasKey(e => e.RefereeId).HasName("PK_Referees");

            entity.ToTable("Referees");

            entity.HasIndex(e => e.ExternalId, "IX_Referees_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.HasIndex(e => e.City, "IX_Referees_City");

            entity.Property(e => e.ExternalId).HasMaxLength(50);

            entity.Property(e => e.FullName).HasMaxLength(150);

            entity.Property(e => e.City).HasMaxLength(100);

            entity.Property(e => e.LevelSingle).HasColumnType("decimal(4, 2)");

            entity.Property(e => e.LevelDouble).HasColumnType("decimal(4, 2)");

            entity.Property(e => e.AvatarUrl).HasMaxLength(500);

            entity.Property(e => e.RefereeType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("REFEREE");

            entity.Property(e => e.Introduction).HasColumnType("nvarchar(max)");

            entity.Property(e => e.WorkingArea).HasColumnType("nvarchar(max)");

            entity.Property(e => e.Achievements).HasColumnType("nvarchar(max)");

            entity.Property(e => e.Verified).HasDefaultValue(false);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0);
        });
        modelBuilder.Entity<TournamentRoundMap>(entity =>
        {
            entity.ToTable("TournamentRoundMaps");

            entity.HasKey(e => e.TournamentRoundMapId);

            entity.HasIndex(e => new { e.TournamentId, e.RoundKey })
                .IsUnique()
                .HasDatabaseName("UX_TRM_Tournament_RoundKey");

            entity.Property(e => e.RoundKey)
                .HasMaxLength(20);

            entity.Property(e => e.RoundLabel)
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Tournament)
                .WithMany() // nếu muốn navigation Tournament.TournamentRoundMaps thì thay tại đây
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TRM_Tournament");
        });
        modelBuilder.Entity<TournamentRoundGroup>(entity =>
        {
            entity.ToTable("TournamentRoundGroups");

            entity.HasKey(e => e.TournamentRoundGroupId);

            entity.HasIndex(e => new { e.TournamentRoundMapId, e.GroupName })
                .IsUnique()
                .HasDatabaseName("UX_TRG_RoundMap_GroupName");

            entity.HasIndex(e => e.TournamentRoundMapId)
                .HasDatabaseName("IX_TRG_RoundMap");

            entity.Property(e => e.GroupName)
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0);

            entity.HasOne(d => d.TournamentRoundMap)
                .WithMany(p => p.TournamentRoundGroups)
                .HasForeignKey(d => d.TournamentRoundMapId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TRG_RoundMap");
        });
        modelBuilder.Entity<TournamentGroupMatch>(entity =>
        {
            entity.ToTable("TournamentGroupMatches");

            entity.HasKey(e => e.MatchId);

            entity.HasIndex(e => new { e.TournamentRoundGroupId, e.StartAt })
                .HasDatabaseName("IX_TGM_Group_StartAt");

            entity.HasIndex(e => e.TournamentId)
                .HasDatabaseName("IX_TGM_Tournament");

            entity.HasIndex(e => e.Team1RegistrationId)
                .HasDatabaseName("IX_TGM_Team1");

            entity.HasIndex(e => e.Team2RegistrationId)
                .HasDatabaseName("IX_TGM_Team2");
            entity.HasIndex(e => e.RefereeUserId)
                .HasDatabaseName("IX_TGM_RefereeUserId");
            entity.HasOne(d => d.RefereeUser)
                .WithMany(p => p.RefereeTournamentGroupMatches)
                .HasForeignKey(d => d.RefereeUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TGM_RefereeUser");
            // Unique pair per group (TeamMin/TeamMax)
            entity.HasIndex(e => new { e.TournamentRoundGroupId, e.TeamMin, e.TeamMax })
                .IsUnique()
                .HasDatabaseName("UX_TGM_Group_TeamPair");

            entity.Property(e => e.AddressText).HasMaxLength(400);
            entity.Property(e => e.CourtText).HasMaxLength(100);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.Property(e => e.IsCompleted).HasDefaultValue(false);
            entity.Property(e => e.ScoreTeam1).HasDefaultValue(0);
            entity.Property(e => e.ScoreTeam2).HasDefaultValue(0);

            // computed persisted columns
            entity.Property(e => e.TeamMin)
                .HasComputedColumnSql("(CASE WHEN [Team1RegistrationId] < [Team2RegistrationId] THEN [Team1RegistrationId] ELSE [Team2RegistrationId] END)", stored: true)
                .ValueGeneratedOnAddOrUpdate();

            entity.Property(e => e.TeamMax)
                .HasComputedColumnSql("(CASE WHEN [Team1RegistrationId] > [Team2RegistrationId] THEN [Team1RegistrationId] ELSE [Team2RegistrationId] END)", stored: true)
                .ValueGeneratedOnAddOrUpdate();

            entity.HasCheckConstraint("CK_TGM_TeamsDifferent", "[Team1RegistrationId] <> [Team2RegistrationId]");

            entity.HasOne(d => d.TournamentRoundGroup)
                .WithMany(p => p.TournamentGroupMatches)
                .HasForeignKey(d => d.TournamentRoundGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TGM_Group");

            entity.HasOne(d => d.Tournament)
                .WithMany()
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TGM_Tournament");

            entity.HasOne(d => d.Team1Registration)
                .WithMany()
                .HasForeignKey(d => d.Team1RegistrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TGM_Team1");

            entity.HasOne(d => d.Team2Registration)
                .WithMany()
                .HasForeignKey(d => d.Team2RegistrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TGM_Team2");

            entity.HasOne(d => d.WinnerRegistration)
                .WithMany()
                .HasForeignKey(d => d.WinnerRegistrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TGM_Winner");
        });
        modelBuilder.Entity<Banner>(entity =>
        {
            entity.HasKey(e => e.BannerId).HasName("PK__Banners__32E86AD111175221");

            entity.HasIndex(e => e.BannerKey, "UQ__Banners__EDD99DA7A8D2B4C6").IsUnique();

            entity.Property(e => e.BannerKey).HasMaxLength(50);
            entity.Property(e => e.ImageUrl).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Title).HasMaxLength(250);
        });

        modelBuilder.Entity<Club>(entity =>
        {
            entity.HasKey(e => e.ClubId).HasName("PK__Clubs__D35058E732853342");

            entity.HasIndex(e => e.ExternalId, "IX_Clubs_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.HasIndex(e => e.OwId, "IX_Clubs_OwId");

            entity.Property(e => e.AreaText).HasMaxLength(200);
            entity.Property(e => e.ClubName).HasMaxLength(200);
            entity.Property(e => e.CoverUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.RatingAvg).HasColumnType("decimal(3, 2)");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.Property(e => e.AllowChallenge)
                .HasDefaultValue(false);

            entity.HasOne(d => d.Ow).WithMany(p => p.Clubs)
                .HasForeignKey(d => d.OwId)
                .HasConstraintName("FK_Clubs_Owner");
        });

        modelBuilder.Entity<ClubMember>(entity =>
        {
            entity.HasKey(e => new { e.ClubId, e.UserId });

            entity.HasIndex(e => e.UserId, "IX_ClubMembers_UserId");

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.JoinedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.MemberRole)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("MEMBER");

            entity.HasOne(d => d.Club).WithMany(p => p.ClubMembers)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClubMembers_Clubs");

            entity.HasOne(d => d.User).WithMany(p => p.ClubMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClubMembers_Users");
        });

        modelBuilder.Entity<ClubMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PK__ClubMess__C87C0C9C96F0EAE7");

            entity.HasIndex(e => new { e.ClubId, e.SentAt }, "IX_ClubMessages_Club_SentAt").IsDescending(false, true);

            entity.HasIndex(e => new { e.SenderUserId, e.SentAt }, "IX_ClubMessages_Sender").IsDescending(false, true);

            entity.Property(e => e.MediaUrl).HasMaxLength(500);
            entity.Property(e => e.MessageType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("TEXT");
            entity.Property(e => e.SentAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Club).WithMany(p => p.ClubMessages)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClubMessages_Clubs");

            entity.HasOne(d => d.ReplyTo).WithMany(p => p.InverseReplyTo)
                .HasForeignKey(d => d.ReplyToId)
                .HasConstraintName("FK_ClubMessages_Reply");

            entity.HasOne(d => d.SenderUser).WithMany(p => p.ClubMessages)
                .HasForeignKey(d => d.SenderUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClubMessages_Sender");
        });

        modelBuilder.Entity<Coach>(entity =>
        {
            entity.HasKey(e => e.CoachId).HasName("PK__Coaches__F411D94141689B06");

            entity.HasIndex(e => e.ExternalId, "IX_Coaches_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CoachType)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("COACH");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.LevelDouble).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.LevelSingle).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.Introduction).HasColumnType("nvarchar(max)");
            entity.Property(e => e.TeachingArea).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Achievements).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<Court>(entity =>
        {
            entity.HasKey(e => e.CourtId).HasName("PK__Courts__C3A67C9A20367AD6");

            entity.HasIndex(e => e.ExternalId, "IX_Courts_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.Property(e => e.AreaText).HasMaxLength(200);
            entity.Property(e => e.CourtName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.ManagerName).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(30);
        });

        modelBuilder.Entity<CourtImage>(entity =>
        {
            entity.HasKey(e => e.CourtImageId).HasName("PK__CourtIma__48D7A3C20BBDFD67");

            entity.HasIndex(e => new { e.CourtId, e.SortOrder }, "IX_CourtImages_CourtId");

            entity.Property(e => e.ImageUrl).HasMaxLength(500);

            entity.HasOne(d => d.Court).WithMany(p => p.CourtImages)
                .HasForeignKey(d => d.CourtId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CourtImages_Courts");
        });

        modelBuilder.Entity<Exchange>(entity =>
        {
            entity.HasKey(e => e.ExchangeId).HasName("PK__Exchange__72E6008BC9A72DA7");

            entity.HasIndex(e => e.ExternalId, "IX_Exchanges_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.Property(e => e.AgoText).HasMaxLength(50);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.LeftClubName).HasMaxLength(200);
            entity.Property(e => e.LeftLogoUrl).HasMaxLength(500);
            entity.Property(e => e.LocationText).HasMaxLength(200);
            entity.Property(e => e.MatchTime).HasPrecision(0);
            entity.Property(e => e.RightClubName).HasMaxLength(200);
            entity.Property(e => e.RightLogoUrl).HasMaxLength(500);
            entity.Property(e => e.ScoreText).HasMaxLength(50);
            entity.Property(e => e.TimeTextRaw).HasMaxLength(30);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A599A826A");

            entity.HasIndex(e => e.RoleCode, "UQ__Roles__D62CB59CCE21DBA1").IsUnique();

            entity.Property(e => e.RoleCode)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.RoleName).HasMaxLength(100);
        });

        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.HasKey(e => e.TournamentId).HasName("PK__Tourname__AC631313C2D70AC1");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_Tournaments_GenderCategory",
                    "[GenderCategory] IN ('OPEN', 'MEN', 'WOMEN', 'MIXED')");

                t.HasCheckConstraint(
                    "CK_Tournaments_GameType_GenderCategory",
                    "((UPPER([GameType]) = 'SINGLE' AND [GenderCategory] IN ('OPEN', 'MEN', 'WOMEN')) OR (UPPER([GameType]) = 'DOUBLE' AND [GenderCategory] IN ('OPEN', 'MEN', 'WOMEN', 'MIXED')))");
            });

            entity.HasIndex(e => e.ExternalId, "IX_Tournaments_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.HasIndex(e => new { e.Status, e.StartTime }, "IX_Tournaments_Status").IsDescending(false, true);

            entity.Property(e => e.AreaText).HasMaxLength(200);
            entity.Property(e => e.BannerUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.CreatorName).HasMaxLength(150);
            entity.Property(e => e.DoubleLimit).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.FormatText).HasMaxLength(50);
            entity.Property(e => e.GameType).HasMaxLength(50);
            entity.Property(e => e.GenderCategory)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("OPEN");
            entity.Property(e => e.LocationText).HasMaxLength(400);
            entity.Property(e => e.Organizer).HasMaxLength(200);
            entity.Property(e => e.PlayoffType).HasMaxLength(50);
            entity.Property(e => e.Remove)
                .HasColumnName("Remove")
                .HasDefaultValue(false);
            entity.Property(e => e.RegisterDeadline).HasPrecision(0);
            entity.Property(e => e.RegisterDeadlineRaw).HasMaxLength(30);
            entity.Property(e => e.SingleLimit).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.StartTime).HasPrecision(0);
            entity.Property(e => e.StartTimeRaw).HasMaxLength(30);
            entity.Property(e => e.StateText).HasMaxLength(50);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.StatusText).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.TournamentRule).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ZaloLink).HasMaxLength(500);
        });

        modelBuilder.Entity<TournamentRegistration>(entity =>
        {
            entity.HasKey(e => e.RegistrationId).HasName("PK__Tourname__6EF5881011DE0049");

            entity.HasIndex(e => e.RegCode, "IX_TournamentRegistrations_RegCode").IsUnique();

            entity.HasIndex(e => new { e.TournamentId, e.RegIndex }, "IX_TournamentRegistrations_Tournament");

            entity.Property(e => e.BtCode).HasMaxLength(50);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.Player1Avatar).HasMaxLength(500);
            entity.Property(e => e.Player1Level).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.Player1Name).HasMaxLength(150);
            entity.Property(e => e.Player2Avatar).HasMaxLength(500);
            entity.Property(e => e.Player2Level).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.Player2Name).HasMaxLength(150);
            entity.Property(e => e.Points).HasColumnType("decimal(6, 2)");
            entity.Property(e => e.RegCode).HasMaxLength(50);
            entity.Property(e => e.RegTime).HasPrecision(0);
            entity.Property(e => e.RegTimeRaw).HasMaxLength(30);

            entity.HasOne(d => d.Player1User).WithMany(p => p.TournamentRegistrationPlayer1Users)
                .HasForeignKey(d => d.Player1UserId)
                .HasConstraintName("FK_Reg_P1User");

            entity.HasOne(d => d.Player2User).WithMany(p => p.TournamentRegistrationPlayer2Users)
                .HasForeignKey(d => d.Player2UserId)
                .HasConstraintName("FK_Reg_P2User");

            entity.HasOne(d => d.Tournament).WithMany(p => p.TournamentRegistrations)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Reg_Tournament");
        });

        modelBuilder.Entity<TournamentPairRequest>(entity =>
        {
            entity.ToTable("TournamentPairRequests", "dbo");

            entity.HasKey(e => e.PairRequestId)
                .HasName("PK_TournamentPairRequests");

            entity.HasIndex(e => new { e.TournamentId, e.RequestedByUserId, e.RequestedToUserId }, "UX_TournamentPairRequests_PendingPair")
                .IsUnique()
                .HasFilter("[Status] = 'PENDING'");

            entity.HasIndex(e => new { e.RequestedToUserId, e.Status, e.RequestedAt }, "IX_TournamentPairRequests_Target_Status_RequestedAt")
                .IsDescending(false, false, true);

            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PENDING");

            entity.Property(e => e.RequestedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.Property(e => e.RespondedAt)
                .HasPrecision(0);

            entity.Property(e => e.ExpiresAt)
                .HasPrecision(0);

            entity.Property(e => e.ResponseNote)
                .HasMaxLength(500);

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_TournamentPairRequests_Status",
                "[Status] IN ('PENDING','ACCEPTED','REJECTED','CANCELED','EXPIRED')"));

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_TournamentPairRequests_NotSelf",
                "[RequestedByUserId] <> [RequestedToUserId]"));

            entity.HasOne(d => d.Tournament)
                .WithMany(p => p.TournamentPairRequests)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TournamentPairRequests_Tournament");

            entity.HasOne(d => d.RequestedByUser)
                .WithMany(p => p.TournamentPairRequestsRequestedBy)
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TournamentPairRequests_RequestedBy");

            entity.HasOne(d => d.RequestedToUser)
                .WithMany(p => p.TournamentPairRequestsRequestedTo)
                .HasForeignKey(d => d.RequestedToUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TournamentPairRequests_RequestedTo");

            entity.HasOne(d => d.Registration)
                .WithMany(p => p.TournamentPairRequests)
                .HasForeignKey(d => d.RegistrationId)
                .HasConstraintName("FK_TournamentPairRequests_Registration");
        });

        modelBuilder.Entity<TournamentRound>(entity =>
        {
            entity.HasKey(e => e.RoundKey);

            entity.Property(e => e.RoundKey).HasMaxLength(20);
            entity.Property(e => e.RoundLabel).HasMaxLength(50);
        });


        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4CC3A0192E");

            entity.HasIndex(e => e.City, "IX_Users_City");

            entity.HasIndex(e => e.ExternalId, "IX_Users_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.FullName).HasMaxLength(150);
            entity.Property(e => e.Gender).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.RatingDouble).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.RatingSingle).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("UserNotifications", "dbo");

            entity.HasKey(e => e.NotificationId)
                .HasName("PK_UserNotifications");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_UserNotifications_User_IsRead_CreatedAt")
                .IsDescending(false, false, true);

            entity.Property(e => e.NotificationType)
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.Property(e => e.Title)
                .HasMaxLength(200);

            entity.Property(e => e.Body)
                .HasMaxLength(1000);

            entity.Property(e => e.RefType)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.Property(e => e.IsRead)
                .HasDefaultValue(false);

            entity.Property(e => e.ReadAt)
                .HasPrecision(0);

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.User)
                .WithMany(p => p.UserNotifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserNotifications_User");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Roles");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRoles_Users");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
