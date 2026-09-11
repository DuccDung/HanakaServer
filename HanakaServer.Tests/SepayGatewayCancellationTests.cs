using HanakaServer.Options;
using HanakaServer.Services.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class SepayGatewayCancellationTests
{
    [Fact]
    public async Task Caller_cancellation_is_propagated_instead_of_returning_fallback()
    {
        using var httpClient = new HttpClient(new CancellationHandler());
        var client = new SepayGatewayClient(
            httpClient,
            NullLogger<SepayGatewayClient>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.PrepareCheckoutAsync(100_000m, "HNK123", Options(), cancellation.Token));
    }

    [Fact]
    public async Task Provider_timeout_uses_configured_receiver_fallback()
    {
        using var httpClient = new HttpClient(new TimeoutHandler());
        var client = new SepayGatewayClient(
            httpClient,
            NullLogger<SepayGatewayClient>.Instance);

        var result = await client.PrepareCheckoutAsync(
            100_000m,
            "HNK123",
            Options(),
            CancellationToken.None);

        Assert.Equal("0123456789", result.AccountNumber);
        Assert.False(result.ResolvedByApi);
    }

    private static SepayOptions Options() => new()
    {
        ApiToken = "test-token",
        ReceiverAccountNumber = "0123456789",
        ReceiverAccountName = "HANAKA TEST",
        ReceiverBankName = "Test Bank",
        ReceiverBankShortName = "TEST"
    };

    private sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("provider timeout"));
    }
}
