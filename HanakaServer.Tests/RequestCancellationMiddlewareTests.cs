using HanakaServer.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RequestCancellationMiddlewareTests
{
    [Fact]
    public async Task Aborted_request_cancellation_is_handled_as_499()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var middleware = Create(_ => throw new TaskCanceledException());

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_not_caused_by_request_abort_is_not_swallowed()
    {
        var context = new DefaultHttpContext();
        var middleware = Create(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<TaskCanceledException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task Non_cancellation_exception_is_not_swallowed()
    {
        var context = new DefaultHttpContext();
        var middleware = Create(_ => throw new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task Aborted_request_does_not_replace_a_started_response_status()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var responseFeature = new StartedResponseFeature(StatusCodes.Status202Accepted);
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);
        context.RequestAborted = cancellation.Token;
        var middleware = Create(_ => throw new OperationCanceledException(cancellation.Token));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
    }

    private static RequestCancellationMiddleware Create(RequestDelegate next) =>
        new(next, NullLogger<RequestCancellationMiddleware>.Instance);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Sql_cancelled_batch_is_handled_only_when_request_was_aborted(bool efWrapped, bool severeMessage)
    {
        Exception error = severeMessage
            ? SqlExceptionTestFactory.Create(
                (0, "A severe error occurred on the current command.  The results, if any, should be discarded."),
                (0, "Operation cancelled by user."))
            : SqlExceptionTestFactory.CancelledBatch();
        if (efWrapped) error = new InvalidOperationException("Transient failure", error);
        var middleware = Create(_ => throw error);
        var liveRequest = new DefaultHttpContext();

        Assert.Same(error, await Assert.ThrowsAnyAsync<Exception>(() => middleware.InvokeAsync(liveRequest)));

        var abortedRequest = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };
        await middleware.InvokeAsync(abortedRequest);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, abortedRequest.Response.StatusCode);
    }

    [Theory]
    [InlineData(-2, "Execution Timeout Expired.")]
    [InlineData(1205, "Transaction was deadlocked and chosen as the deadlock victim.")]
    [InlineData(208, "Invalid object name 'dbo.UserRatingHistory'.")]
    [InlineData(0, "A different provider error.")]
    public async Task Sql_cancellation_does_not_hide_additional_database_errors(int number, string message)
    {
        var sql = SqlExceptionTestFactory.Create(
            (3980, "Batch aborted"), (0, "Operation cancelled by user."), (number, message));
        var error = new InvalidOperationException("Transient failure", sql);
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => Create(_ => throw error).InvokeAsync(request)));
    }

    [Fact]
    public async Task Busy_session_is_not_mistaken_for_cancellation_even_after_request_abort()
    {
        var sql = SqlExceptionTestFactory.Create((3980, "Another request is running in the same session."));
        var error = new InvalidOperationException("Transient failure", sql);
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => Create(_ => throw error).InvokeAsync(request)));
    }

    [Fact]
    public async Task Severe_sql_error_without_cancellation_marker_is_not_swallowed()
    {
        var error = SqlExceptionTestFactory.Create(
            (0, "A severe error occurred on the current command.  The results, if any, should be discarded."));
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        Assert.Same(error, await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(
            () => Create(_ => throw error).InvokeAsync(request)));
    }

    [Fact]
    public async Task Aborted_request_does_not_hide_unrelated_application_failure()
    {
        var error = new InvalidOperationException("Application failure");
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => Create(_ => throw error).InvokeAsync(request)));
    }

    [Fact]
    public async Task Sql_cancellation_preserves_response_that_already_started()
    {
        var request = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };
        request.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(
            new StartedResponseFeature(StatusCodes.Status202Accepted));

        await Create(_ => throw new InvalidOperationException("Transient failure", SqlExceptionTestFactory.CancelledBatch()))
            .InvokeAsync(request);

        Assert.Equal(StatusCodes.Status202Accepted, request.Response.StatusCode);
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public StartedResponseFeature(int statusCode)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; set; }
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
