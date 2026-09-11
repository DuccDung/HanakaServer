using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HanakaServer.Helpers;

public static class CookieAuthenticationResponseHandler
{
    public static Task HandleRedirectToLoginAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/RatingPortal"))
        {
            context.Response.Redirect("/RatingPortal/Login");
            return Task.CompletedTask;
        }

        if (IsCookieAuthenticatedApi(context.Request.Path))
        {
            return WriteApiErrorAsync(
                context.Response,
                StatusCodes.Status401Unauthorized,
                "Phiên đăng nhập đã hết hạn hoặc không hợp lệ.");
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public static Task HandleRedirectToAccessDeniedAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsCookieAuthenticatedApi(context.Request.Path))
        {
            return WriteApiErrorAsync(
                context.Response,
                StatusCodes.Status403Forbidden,
                "Tài khoản không có quyền thực hiện thao tác này.");
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public static bool IsCookieAuthenticatedApi(PathString path)
    {
        return path.StartsWithSegments("/api/rating-auth")
            || path.StartsWithSegments("/api/rating-assessment")
            || path.StartsWithSegments("/api/referee-auth")
            || path.StartsWithSegments("/api/referee");
    }

    private static Task WriteApiErrorAsync(HttpResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        return response.WriteAsJsonAsync(new { message });
    }
}
