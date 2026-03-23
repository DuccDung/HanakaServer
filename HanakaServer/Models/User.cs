using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class User
{
    public long UserId { get; set; }

    public string? ExternalId { get; set; }

    public string FullName { get; set; } = null!;

    public string? City { get; set; }

    public string? Gender { get; set; }

    public bool Verified { get; set; }

    public decimal? RatingSingle { get; set; }

    public decimal? RatingDouble { get; set; }

    public string? AvatarUrl { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? Bio { get; set; }
    public DateTime? BirthOfDate { get; set; }

    public virtual ICollection<ClubMember> ClubMembers { get; set; } = new List<ClubMember>();

    public virtual ICollection<ClubMessage> ClubMessages { get; set; } = new List<ClubMessage>();

    public virtual ICollection<Club> Clubs { get; set; } = new List<Club>();

    public virtual ICollection<TournamentRegistration> TournamentRegistrationPlayer1Users { get; set; } = new List<TournamentRegistration>();

    public virtual ICollection<TournamentRegistration> TournamentRegistrationPlayer2Users { get; set; } = new List<TournamentRegistration>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public virtual ICollection<UserOtp> UserOtps { get; set; } = new List<UserOtp>();

    public virtual ICollection<UserRatingHistory> UserRatingHistories { get; set; } = new List<UserRatingHistory>();

    public virtual ICollection<UserAchievement> UserAchievements { get; set; } = new List<UserAchievement>();
    public virtual ICollection<TournamentGroupMatch> RefereeTournamentGroupMatches { get; set; } = new List<TournamentGroupMatch>();
    public virtual ICollection<TournamentMatchScoreHistory> RefereeScoreHistories { get; set; } = new List<TournamentMatchScoreHistory>();
}
