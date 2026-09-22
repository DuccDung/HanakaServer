using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Data;

public partial class PickleballDbContext
{
    public DbSet<TournamentCoordinator> TournamentCoordinators { get; set; }
    public DbSet<MatchCoordinationHistory> MatchCoordinationHistories { get; set; }

    private static void ConfigureCoordination(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TournamentGroupMatch>(e =>
        {
            e.Property(x => x.MatchStatus).HasMaxLength(20).IsUnicode(false).HasDefaultValue(MatchStatuses.NotStarted);
            e.Property(x => x.StateVersion).IsConcurrencyToken();
        });
        modelBuilder.Entity<TournamentCoordinator>(e =>
        {
            e.ToTable("TournamentCoordinators");
            e.HasKey(x => new { x.TournamentId, x.UserId });
            e.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MatchCoordinationHistory>(e =>
        {
            e.ToTable("MatchCoordinationHistories");
            e.HasKey(x => x.Id);
            e.Property(x => x.PreviousCourt).HasMaxLength(200);
            e.Property(x => x.Court).HasMaxLength(200);
            e.Property(x => x.PreviousStatus).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            e.HasIndex(x => new { x.MatchId, x.CreatedAt });
            e.HasOne<TournamentGroupMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareMatchStates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareMatchStates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareMatchStates()
    {
        ChangeTracker.DetectChanges();
        var scored = ChangeTracker.Entries<TournamentMatchScoreHistory>()
            .Where(x => x.State == EntityState.Added).Select(x => x.Entity.MatchId).ToHashSet();
        foreach (var entry in ChangeTracker.Entries<TournamentGroupMatch>().ToArray())
        {
            if (entry.State is EntityState.Deleted or EntityState.Detached) continue;
            var match = entry.Entity;
            var scoring = scored.Contains(match.MatchId);
            if (entry.State == EntityState.Unchanged && !scoring) continue;
            var participantsChanged = entry.State != EntityState.Added &&
                (entry.Property(x => x.Team1RegistrationId).IsModified || entry.Property(x => x.Team2RegistrationId).IsModified);
            if (match.IsCompleted) match.MatchStatus = MatchStatuses.Completed;
            else if (participantsChanged || entry.Property(x => x.CompletionReason).OriginalValue == MatchCompletionReasons.Bye)
                match.MatchStatus = MatchStatuses.NotStarted;
            else if (scoring || entry.Property(x => x.IsCompleted).OriginalValue
                || entry.Property(x => x.ScoreTeam1).IsModified || entry.Property(x => x.ScoreTeam2).IsModified
                || (entry.State == EntityState.Added && (match.ScoreTeam1 != 0 || match.ScoreTeam2 != 0)))
                match.MatchStatus = MatchStatuses.InProgress;
            match.StateVersion = entry.State == EntityState.Added ? 1 : checked(entry.Property(x => x.StateVersion).OriginalValue + 1);
            match.UpdatedAt = DateTime.UtcNow;
        }
    }
}
