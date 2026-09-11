using Microsoft.Data.SqlClient;

namespace HanakaServer.Middleware;

/// <summary>
/// Treats a cancellation caused by an aborted HTTP request as a normal end of
/// that request. Cancellations from other sources are deliberately allowed to
/// propagate so provider timeouts and application failures stay observable.
/// </summary>
public sealed class RequestCancellationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestCancellationMiddleware> _logger;

    public RequestCancellationMiddleware(
        RequestDelegate next,
        ILogger<RequestCancellationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (IsRequestCancellation(ex, context.RequestAborted))
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            _logger.LogDebug(
                "HTTP request {Method} {Path} was cancelled because the client disconnected. TraceId: {TraceId}. SqlErrorNumber: {SqlErrorNumber}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier,
                (ex as SqlException ?? ex.InnerException as SqlException)?.Number);
        }
    }

    private static bool IsRequestCancellation(Exception exception, CancellationToken requestAborted)
    {
        if (!requestAborted.IsCancellationRequested)
            return false;

        if (exception is OperationCanceledException)
            return true;

        // EF's non-retrying SQL strategy wraps error 3980 in InvalidOperationException.
        // Cancellation can also produce SQL 0/class 11 with a generic severe-error message.
        // Both variants require the driver's explicit cancellation marker. Preserve any
        // other SQL error, even when the request has meanwhile been aborted.
        var sqlException = exception as SqlException
            ?? (exception as InvalidOperationException)?.InnerException as SqlException;
        if (sqlException == null)
            return false;

        var commandCancelled = false;
        foreach (SqlError error in sqlException.Errors)
        {
            if (error.Number == 3980)
                continue;
            else if (error.Number == 0 && string.Equals(
                error.Message.Trim(), "Operation cancelled by user.", StringComparison.OrdinalIgnoreCase))
                commandCancelled = true;
            else if (error.Number == 0 && error.Class == 11 && error.State == 0 && string.Equals(
                error.Message.Trim(),
                "A severe error occurred on the current command.  The results, if any, should be discarded.",
                StringComparison.OrdinalIgnoreCase))
                continue;
            else
                return false;
        }

        return commandCancelled;
    }
}
