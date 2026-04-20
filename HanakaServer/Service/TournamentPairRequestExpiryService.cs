using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services;

public sealed class TournamentPairRequestExpiryService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RealtimeHub _realtimeHub;
    private readonly ILogger<TournamentPairRequestExpiryService> _logger;

    public TournamentPairRequestExpiryService(
        IServiceScopeFactory scopeFactory,
        RealtimeHub realtimeHub,
        ILogger<TournamentPairRequestExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _realtimeHub = realtimeHub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpirePendingRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not expire tournament pair requests.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task ExpirePendingRequestsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballDbContext>();

        var now = DateTime.UtcNow;
        var expired = await db.TournamentPairRequests
            .Include(x => x.Tournament)
            .Include(x => x.RequestedToUser)
            .Where(x =>
                x.Status == "PENDING" &&
                x.ExpiresAt.HasValue &&
                x.ExpiresAt.Value <= now)
            .Take(100)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        foreach (var request in expired)
        {
            request.Status = "EXPIRED";
            request.RespondedAt = now;

            var notification = new UserNotification
            {
                UserId = request.RequestedByUserId,
                NotificationType = "PAIR_EXPIRED",
                Title = "Lời mời ghép đôi đã hết hạn",
                Body = $"Lời mời gửi cho {request.RequestedToUser.FullName} tại giải {request.Tournament.Title} đã hết hạn.",
                RefType = "PAIR_REQUEST",
                RefId = request.PairRequestId,
                CreatedAt = now,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    request.PairRequestId,
                    request.TournamentId,
                    request.Tournament.Title,
                    requestedTo = new
                    {
                        request.RequestedToUser.UserId,
                        request.RequestedToUser.FullName
                    }
                }, JsonOpts)
            };

            db.UserNotifications.Add(notification);
        }

        await db.SaveChangesAsync(ct);

        foreach (var request in expired)
        {
            await _realtimeHub.SendTournamentNotificationToUserAsync(
                request.RequestedByUserId.ToString(),
                new
                {
                    NotificationType = "PAIR_EXPIRED",
                    Title = "Lời mời ghép đôi đã hết hạn",
                    request.TournamentId,
                    request.PairRequestId
                });
        }
    }
}
