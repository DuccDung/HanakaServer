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

    public virtual DbSet<Coach> Coaches { get; set; }

    public virtual DbSet<Court> Courts { get; set; }

    public virtual DbSet<CourtImage> CourtImages { get; set; }

    public virtual DbSet<Exchange> Exchanges { get; set; }

    public virtual DbSet<Match> Matches { get; set; }

    public virtual DbSet<MatchPlayer> MatchPlayers { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Tournament> Tournaments { get; set; }

    public virtual DbSet<TournamentRegistration> TournamentRegistrations { get; set; }

    public virtual DbSet<TournamentRound> TournamentRounds { get; set; }

    public virtual DbSet<TournamentSchedule> TournamentSchedules { get; set; }

    public virtual DbSet<TournamentStandingGroup> TournamentStandingGroups { get; set; }

    public virtual DbSet<TournamentStandingRow> TournamentStandingRows { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<Video> Videos { get; set; }

    public virtual DbSet<VideoPlayer> VideoPlayers { get; set; }
    public virtual DbSet<TournamentRoundMap> TournamentRoundMaps { get; set; }
    public virtual DbSet<TournamentRoundGroup> TournamentRoundGroups { get; set; }
    public virtual DbSet<TournamentGroupMatch> TournamentGroupMatches { get; set; }
    public virtual DbSet<Referee> Referees { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

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

        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.MatchId).HasName("PK__Matches__4218C8176792A2AF");

            entity.HasIndex(e => e.ExternalId, "IX_Matches_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.HasIndex(e => e.MatchTime, "IX_Matches_Time").IsDescending();

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysdatetime())");
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.MatchTime).HasPrecision(0);
            entity.Property(e => e.MatchTimeRaw).HasMaxLength(30);
            entity.Property(e => e.MatchType)
                .HasMaxLength(10)
                .IsUnicode(false);
        });

        modelBuilder.Entity<MatchPlayer>(entity =>
        {
            entity.HasKey(e => new { e.MatchId, e.Side, e.Slot });

            entity.Property(e => e.Side)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.DisplayName).HasMaxLength(150);

            entity.HasOne(d => d.Match).WithMany(p => p.MatchPlayers)
                .HasForeignKey(d => d.MatchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MatchPlayers_Matches");

            entity.HasOne(d => d.User).WithMany(p => p.MatchPlayers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_MatchPlayers_Users");
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
            entity.Property(e => e.LocationText).HasMaxLength(400);
            entity.Property(e => e.Organizer).HasMaxLength(200);
            entity.Property(e => e.PlayoffType).HasMaxLength(50);
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

        modelBuilder.Entity<TournamentRound>(entity =>
        {
            entity.HasKey(e => e.RoundKey);

            entity.Property(e => e.RoundKey).HasMaxLength(20);
            entity.Property(e => e.RoundLabel).HasMaxLength(50);
        });

        modelBuilder.Entity<TournamentSchedule>(entity =>
        {
            entity.HasKey(e => e.ScheduleId);
            entity.ToTable("TournamentSchedule");

            entity.Property(e => e.RoundKey).HasMaxLength(20);
            entity.Property(e => e.Code).HasMaxLength(50);

            // NEW
            entity.Property(e => e.StandingGroupId);

            entity.Property(e => e.TimeText).HasMaxLength(20);
            entity.Property(e => e.CourtText).HasMaxLength(100);
            entity.Property(e => e.TeamA).HasMaxLength(200);
            entity.Property(e => e.TeamB).HasMaxLength(200);

            entity.HasOne(d => d.RoundKeyNavigation)
                .WithMany(p => p.TournamentSchedules)
                .HasForeignKey(d => d.RoundKey)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TS_Round");

            entity.HasOne(d => d.Tournament)
                .WithMany(p => p.TournamentSchedules)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TS_Tournament");

            // optional FK:
            // entity.HasOne<TournamentStandingGroup>()
            //   .WithMany()
            //   .HasForeignKey(x => x.StandingGroupId)
            //   .HasConstraintName("FK_TS_Group");
        });

        modelBuilder.Entity<TournamentStandingGroup>(entity =>
        {
            entity.HasKey(e => e.StandingGroupId).HasName("PK__Tourname__DFD000C172358169");

            entity.HasIndex(e => new { e.TournamentId, e.RoundKey }, "IX_TournamentStandingGroups_TR");

            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.GroupName).HasMaxLength(100);
            entity.Property(e => e.RoundKey).HasMaxLength(20);

            entity.HasOne(d => d.RoundKeyNavigation).WithMany(p => p.TournamentStandingGroups)
                .HasForeignKey(d => d.RoundKey)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TSG_Round");

            entity.HasOne(d => d.Tournament).WithMany(p => p.TournamentStandingGroups)
                .HasForeignKey(d => d.TournamentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TSG_Tournament");
        });

        modelBuilder.Entity<TournamentStandingRow>(entity =>
        {
            entity.HasKey(e => e.StandingRowId).HasName("PK__Tourname__F7BF140CAD47FB10");

            entity.HasIndex(e => new { e.StandingGroupId, e.Rank }, "IX_TournamentStandingRows_Group");

            entity.Property(e => e.TeamText).HasMaxLength(250);

            entity.HasOne(d => d.StandingGroup).WithMany(p => p.TournamentStandingRows)
                .HasForeignKey(d => d.StandingGroupId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TSR_Group");
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

        modelBuilder.Entity<Video>(entity =>
        {
            entity.HasKey(e => e.VideoId).HasName("PK__Videos__BAE5126AEDEE3982");

            entity.HasIndex(e => e.ExternalId, "IX_Videos_ExternalId")
                .IsUnique()
                .HasFilter("([ExternalId] IS NOT NULL)");

            entity.HasIndex(e => e.VideoTime, "IX_Videos_Time").IsDescending();

            entity.Property(e => e.BannerUrl).HasMaxLength(500);
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.DateTimeRaw).HasMaxLength(30);
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(250);
            entity.Property(e => e.VideoTime).HasPrecision(0);
            entity.Property(e => e.VideoType)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VideoPlayer>(entity =>
        {
            entity.HasKey(e => new { e.VideoId, e.Slot });

            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.DisplayName).HasMaxLength(150);

            entity.HasOne(d => d.User).WithMany(p => p.VideoPlayers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_VideoPlayers_Users");

            entity.HasOne(d => d.Video).WithMany(p => p.VideoPlayers)
                .HasForeignKey(d => d.VideoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VideoPlayers_Videos");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
