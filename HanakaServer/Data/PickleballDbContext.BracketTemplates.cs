using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Data;

public partial class PickleballDbContext
{
    public virtual DbSet<BracketTemplate> BracketTemplates { get; set; }
    public virtual DbSet<BracketTemplateVersion> BracketTemplateVersions { get; set; }
    public virtual DbSet<BracketTemplateRound> BracketTemplateRounds { get; set; }
    public virtual DbSet<BracketTemplateGroup> BracketTemplateGroups { get; set; }
    public virtual DbSet<BracketTemplateMatch> BracketTemplateMatches { get; set; }
    public virtual DbSet<BracketTemplateMatchSlot> BracketTemplateMatchSlots { get; set; }
    public virtual DbSet<TournamentBracketApplication> TournamentBracketApplications { get; set; }
    public virtual DbSet<TournamentBracketSeedAssignment> TournamentBracketSeedAssignments { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureBracketTemplate(modelBuilder);
        ConfigureBracketTemplateVersion(modelBuilder);
        ConfigureBracketTemplateRound(modelBuilder);
        ConfigureBracketTemplateGroup(modelBuilder);
        ConfigureBracketTemplateMatch(modelBuilder);
        ConfigureBracketTemplateMatchSlot(modelBuilder);
        ConfigureTournamentBracketApplication(modelBuilder);
        ConfigureTournamentBracketSeedAssignment(modelBuilder);
        ConfigureBracketRuntimeExtensions(modelBuilder);
    }

    private static void ConfigureBracketTemplate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplate>(entity =>
        {
            entity.ToTable("BracketTemplates");
            entity.HasKey(x => x.BracketTemplateId).HasName("PK_BracketTemplates");

            entity.HasIndex(x => x.TemplateCode)
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplates_TemplateCode");
            entity.HasIndex(x => new { x.Status, x.FormatType })
                .HasDatabaseName("IX_BracketTemplates_Status_FormatType");

            entity.Property(x => x.TemplateCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TemplateName).HasMaxLength(150);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.FormatType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).HasDefaultValue(BracketTemplateStatuses.Draft);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplates_CreatedByUser");
            entity.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplates_UpdatedByUser");
            entity.HasOne(x => x.CurrentPublishedVersion)
                .WithMany()
                .HasForeignKey(x => x.CurrentPublishedVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplates_CurrentPublishedVersion");
        });
    }

    private static void ConfigureBracketTemplateVersion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplateVersion>(entity =>
        {
            entity.ToTable("BracketTemplateVersions");
            entity.HasKey(x => x.BracketTemplateVersionId).HasName("PK_BracketTemplateVersions");

            entity.HasIndex(x => new { x.BracketTemplateId, x.VersionNumber })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateVersions_Template_Version");
            entity.HasIndex(x => x.BracketTemplateId)
                .IsUnique()
                .HasFilter("[Status] = 'DRAFT'")
                .HasDatabaseName("UX_BracketTemplateVersions_OneDraft");
            entity.HasIndex(x => new { x.BracketTemplateId, x.Status, x.VersionNumber })
                .HasDatabaseName("IX_BracketTemplateVersions_Template_Status");

            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).HasDefaultValue(BracketTemplateStatuses.Draft);
            entity.Property(x => x.DefaultSeedingMethod).HasMaxLength(30).IsUnicode(false)
                .HasDefaultValue(BracketSeedingMethods.RegistrationOrder);
            entity.Property(x => x.ConfigurationHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.DraftGraphJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);
            entity.Property(x => x.PublishedAt).HasPrecision(0);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            entity.HasOne(x => x.BracketTemplate)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.BracketTemplateId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BracketTemplateVersions_Template");
            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplateVersions_CreatedByUser");
            entity.HasOne(x => x.PublishedByUser)
                .WithMany()
                .HasForeignKey(x => x.PublishedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplateVersions_PublishedByUser");
        });
    }

    private static void ConfigureBracketTemplateRound(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplateRound>(entity =>
        {
            entity.ToTable("BracketTemplateRounds");
            entity.HasKey(x => x.BracketTemplateRoundId).HasName("PK_BracketTemplateRounds");
            entity.HasIndex(x => new { x.BracketTemplateVersionId, x.RoundKey })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateRounds_Version_RoundKey");
            entity.HasIndex(x => new { x.BracketTemplateVersionId, x.SortOrder, x.RoundKey })
                .HasDatabaseName("IX_BracketTemplateRounds_Version_SortOrder");

            entity.Property(x => x.RoundKey).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.RoundLabel).HasMaxLength(50);
            entity.Property(x => x.RoundType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);

            entity.HasOne(x => x.BracketTemplateVersion)
                .WithMany(x => x.Rounds)
                .HasForeignKey(x => x.BracketTemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BracketTemplateRounds_Version");
        });
    }

    private static void ConfigureBracketTemplateGroup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplateGroup>(entity =>
        {
            entity.ToTable("BracketTemplateGroups");
            entity.HasKey(x => x.BracketTemplateGroupId).HasName("PK_BracketTemplateGroups");
            entity.HasIndex(x => new { x.BracketTemplateVersionId, x.GroupKey })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateGroups_Version_GroupKey");
            entity.HasIndex(x => new { x.BracketTemplateRoundId, x.GroupName })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateGroups_Round_GroupName");
            entity.HasIndex(x => new { x.BracketTemplateRoundId, x.SortOrder, x.GroupKey })
                .HasDatabaseName("IX_BracketTemplateGroups_Round_SortOrder");

            entity.Property(x => x.GroupKey).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.GroupName).HasMaxLength(50);
            entity.Property(x => x.GroupType).HasMaxLength(30).IsUnicode(false).HasDefaultValue(BracketGroupTypes.Generic);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);

            entity.HasOne(x => x.BracketTemplateRound)
                .WithMany(x => x.Groups)
                .HasForeignKey(x => x.BracketTemplateRoundId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BracketTemplateGroups_RoundVersion");
        });
    }

    private static void ConfigureBracketTemplateMatch(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplateMatch>(entity =>
        {
            entity.ToTable("BracketTemplateMatches");
            entity.HasKey(x => x.BracketTemplateMatchId).HasName("PK_BracketTemplateMatches");
            entity.HasIndex(x => new { x.BracketTemplateVersionId, x.MatchKey })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateMatches_Version_MatchKey");
            entity.HasIndex(x => new { x.BracketTemplateGroupId, x.SortOrder, x.MatchKey })
                .HasDatabaseName("IX_BracketTemplateMatches_Group_SortOrder");

            entity.Property(x => x.MatchKey).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.MatchLabel).HasMaxLength(100);
            entity.Property(x => x.TerminalType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);

            entity.HasOne(x => x.BracketTemplateGroup)
                .WithMany(x => x.Matches)
                .HasForeignKey(x => x.BracketTemplateGroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BracketTemplateMatches_GroupVersion");
        });
    }

    private static void ConfigureBracketTemplateMatchSlot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BracketTemplateMatchSlot>(entity =>
        {
            entity.ToTable("BracketTemplateMatchSlots");
            entity.HasKey(x => x.BracketTemplateMatchSlotId).HasName("PK_BracketTemplateMatchSlots");
            entity.HasIndex(x => new { x.BracketTemplateMatchId, x.SlotNumber })
                .IsUnique()
                .HasDatabaseName("UX_BracketTemplateMatchSlots_Match_Slot");

            entity.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.UpdatedAt).HasPrecision(0);

            entity.HasOne(x => x.BracketTemplateMatch)
                .WithMany(x => x.Slots)
                .HasForeignKey(x => x.BracketTemplateMatchId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_BracketTemplateMatchSlots_MatchVersion");
            entity.HasOne(x => x.SourceMatch)
                .WithMany(x => x.SourceSlots)
                .HasForeignKey(x => x.SourceMatchId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplateMatchSlots_SourceMatchVersion");
            entity.HasOne(x => x.SourceGroup)
                .WithMany(x => x.SourceSlots)
                .HasForeignKey(x => x.SourceGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_BracketTemplateMatchSlots_SourceGroupVersion");
        });
    }

    private static void ConfigureTournamentBracketApplication(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TournamentBracketApplication>(entity =>
        {
            entity.ToTable("TournamentBracketApplications");
            entity.HasKey(x => x.TournamentBracketApplicationId).HasName("PK_TournamentBracketApplications");
            entity.HasIndex(x => x.TournamentId)
                .IsUnique()
                .HasFilter("[IsActive] = 1")
                .HasDatabaseName("UX_TournamentBracketApplications_ActiveTournament");
            entity.HasIndex(x => new { x.BracketTemplateVersionId, x.Status, x.CreatedAt })
                .HasDatabaseName("IX_TournamentBracketApplications_TemplateVersion_Status");
            entity.HasIndex(x => new { x.TournamentId, x.CreatedAt })
                .HasDatabaseName("IX_TournamentBracketApplications_Tournament_History");

            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).HasDefaultValue(BracketApplicationStatuses.Applying);
            entity.Property(x => x.SeedingMethod).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.PreviewHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.RevertReason).HasMaxLength(1000);
            entity.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");
            entity.Property(x => x.AppliedAt).HasPrecision(0);
            entity.Property(x => x.RevertedAt).HasPrecision(0);
            entity.Property(x => x.UpdatedAt).HasPrecision(0);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

            entity.HasOne(x => x.Tournament)
                .WithMany(x => x.BracketApplications)
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentBracketApplications_Tournament");
            entity.HasOne(x => x.BracketTemplate)
                .WithMany(x => x.Applications)
                .HasForeignKey(x => x.BracketTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.BracketTemplateVersion)
                .WithMany(x => x.Applications)
                .HasForeignKey(x => x.BracketTemplateVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentBracketApplications_TemplateVersion");
            entity.HasOne(x => x.AppliedByUser)
                .WithMany()
                .HasForeignKey(x => x.AppliedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentBracketApplications_AppliedByUser");
            entity.HasOne(x => x.RevertedByUser)
                .WithMany()
                .HasForeignKey(x => x.RevertedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentBracketApplications_RevertedByUser");
        });
    }

    private static void ConfigureTournamentBracketSeedAssignment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TournamentBracketSeedAssignment>(entity =>
        {
            entity.ToTable("TournamentBracketSeedAssignments");
            entity.HasKey(x => x.TournamentBracketSeedAssignmentId).HasName("PK_TournamentBracketSeedAssignments");
            entity.HasIndex(x => new { x.TournamentBracketApplicationId, x.SeedNumber })
                .IsUnique()
                .HasDatabaseName("UX_TournamentBracketSeedAssignments_Application_Seed");
            entity.HasIndex(x => new { x.TournamentBracketApplicationId, x.RegistrationId })
                .IsUnique()
                .HasFilter("[RegistrationId] IS NOT NULL")
                .HasDatabaseName("UX_TournamentBracketSeedAssignments_Application_Registration");

            entity.Property(x => x.AssignmentMethod).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.RegistrationCodeSnapshot).HasMaxLength(50);
            entity.Property(x => x.Player1NameSnapshot).HasMaxLength(150);
            entity.Property(x => x.Player2NameSnapshot).HasMaxLength(150);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("(sysdatetime())");

            entity.HasOne(x => x.TournamentBracketApplication)
                .WithMany(x => x.SeedAssignments)
                .HasForeignKey(x => x.TournamentBracketApplicationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TournamentBracketSeedAssignments_Application");
            entity.HasOne(x => x.Registration)
                .WithMany()
                .HasForeignKey(x => x.RegistrationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentBracketSeedAssignments_Registration");
        });
    }

    private static void ConfigureBracketRuntimeExtensions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.Property(x => x.RegistrationLockedAt).HasPrecision(0);
            entity.HasOne(x => x.RegistrationLockedByUser)
                .WithMany()
                .HasForeignKey(x => x.RegistrationLockedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Tournaments_RegistrationLockedByUser");
        });

        modelBuilder.Entity<TournamentRoundMap>(entity =>
        {
            entity.Property(x => x.TemplateRoundKey).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.TemplateRoundType).HasMaxLength(30).IsUnicode(false);
            entity.HasIndex(x => new { x.BracketApplicationId, x.TemplateRoundKey })
                .IsUnique()
                .HasFilter("[BracketApplicationId] IS NOT NULL AND [TemplateRoundKey] IS NOT NULL")
                .HasDatabaseName("UX_TournamentRoundMaps_Application_TemplateKey");
            entity.HasOne(x => x.BracketApplication)
                .WithMany(x => x.GeneratedRounds)
                .HasForeignKey(x => x.BracketApplicationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentRoundMaps_BracketApplication");
        });

        modelBuilder.Entity<TournamentRoundGroup>(entity =>
        {
            entity.Property(x => x.TemplateGroupKey).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TemplateGroupType).HasMaxLength(30).IsUnicode(false);
            entity.HasIndex(x => new { x.BracketApplicationId, x.TemplateGroupKey })
                .IsUnique()
                .HasFilter("[BracketApplicationId] IS NOT NULL AND [TemplateGroupKey] IS NOT NULL")
                .HasDatabaseName("UX_TournamentRoundGroups_Application_TemplateKey");
            entity.HasOne(x => x.BracketApplication)
                .WithMany(x => x.GeneratedGroups)
                .HasForeignKey(x => x.BracketApplicationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentRoundGroups_BracketApplication");
        });

        modelBuilder.Entity<TournamentGroupMatch>(entity =>
        {
            entity.Property(x => x.TemplateMatchKey).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TemplateMatchLabel).HasMaxLength(100);
            entity.Property(x => x.TemplateTerminalType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CompletionReason).HasMaxLength(20).IsUnicode(false);
            entity.HasIndex(x => new { x.BracketApplicationId, x.TemplateMatchKey })
                .IsUnique()
                .HasFilter("[BracketApplicationId] IS NOT NULL AND [TemplateMatchKey] IS NOT NULL")
                .HasDatabaseName("UX_TournamentGroupMatches_Application_TemplateKey");
            entity.HasIndex(x => new { x.BracketApplicationId, x.IsCompleted, x.CompletionReason })
                .HasFilter("[BracketApplicationId] IS NOT NULL")
                .HasDatabaseName("IX_TournamentGroupMatches_Application_Completion");
            entity.HasOne(x => x.BracketApplication)
                .WithMany(x => x.GeneratedMatches)
                .HasForeignKey(x => x.BracketApplicationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_TournamentGroupMatches_BracketApplication");
        });
    }
}
