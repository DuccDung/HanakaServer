using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Middleware;

public sealed class MatchConcurrencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (DbUpdateConcurrencyException ex) when (!context.Response.HasStarted
            && ex.Entries.Any(x => x.Entity is TournamentGroupMatch))
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { code = "MATCH_CHANGED", message = "Trận đã được cập nhật. Vui lòng tải lại trước khi lưu." }, context.RequestAborted);
        }
    }
}
