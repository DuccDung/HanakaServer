using System.Text.Json;
using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HanakaServer.Tests;

public sealed class CookieAuthenticationResponseHandlerTests
{
    [Theory]
    [InlineData("/api/referee/matches")]
    [InlineData("/api/referee/matches/15/score")]
    [InlineData("/api/referee-auth/me")]
    [InlineData("/api/rating-auth/me")]
    [InlineData("/api/rating-assessment/requests")]
    public void IsCookieAuthenticatedApi_recognizes_cookie_api_routes(string path)
    {
        Assert.True(CookieAuthenticationResponseHandler.IsCookieAuthenticatedApi(path));
    }

    [Theory]
    [InlineData("/RefereePortal/Matches")]
    [InlineData("/RatingPortal/Dashboard")]
    [InlineData("/api/public/tournaments")]
    public void IsCookieAuthenticatedApi_ignores_mvc_and_unrelated_api_routes(string path)
    {
        Assert.False(CookieAuthenticationResponseHandler.IsCookieAuthenticatedApi(path));
    }

    [Fact]
    public async Task Referee_api_login_challenge_returns_401_json_without_redirect()
    {
        var context = CreateRedirectContext("/api/referee/matches", "/RefereePortal/Login?ReturnUrl=%2Fapi%2Freferee%2Fmatches");

        await CookieAuthenticationResponseHandler.HandleRedirectToLoginAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.Equal(
            "Phiên đăng nhập đã hết hạn hoặc không hợp lệ.",
            await ReadMessageAsync(context.Response));
    }

    [Fact]
    public async Task Referee_api_access_denied_returns_403_json_without_redirect()
    {
        var context = CreateRedirectContext("/api/referee/matches/15/score", "/RefereePortal/Login");

        await CookieAuthenticationResponseHandler.HandleRedirectToAccessDeniedAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.Equal(
            "Tài khoản không có quyền thực hiện thao tác này.",
            await ReadMessageAsync(context.Response));
    }

    [Fact]
    public async Task Referee_mvc_challenge_keeps_normal_login_redirect()
    {
        const string redirectUri = "/RefereePortal/Login?ReturnUrl=%2FRefereePortal%2FMatches";
        var context = CreateRedirectContext("/RefereePortal/Matches", redirectUri);

        await CookieAuthenticationResponseHandler.HandleRedirectToLoginAsync(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(redirectUri, context.Response.Headers.Location.ToString());
    }

    private static RedirectContext<CookieAuthenticationOptions> CreateRedirectContext(
        string requestPath,
        string redirectUri)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = requestPath;
        httpContext.Response.Body = new MemoryStream();

        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme,
            typeof(CookieAuthenticationHandler));

        return new RedirectContext<CookieAuthenticationOptions>(
            httpContext,
            scheme,
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            redirectUri);
    }

    private static async Task<string?> ReadMessageAsync(HttpResponse response)
    {
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        return document.RootElement.GetProperty("message").GetString();
    }
}
